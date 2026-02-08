using Amazon.Lambda.Core;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using AlgoXera.Lambda.TemplateEngine.DTOs;
using AlgoXera.Lambda.TemplateEngine.Entities;
using AlgoXera.Lambda.TemplateEngine.Models;
using AlgoXera.Lambda.TemplateEngine.Repositories;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace AlgoXera.Lambda.TemplateEngine.Services;

public class TemplateService : ITemplateService
{
    private readonly ITemplateRepository _templateRepository;
    private readonly IAITemplateService _aiService;
    private readonly ILambdaContext _context;
    private readonly IAmazonDynamoDB? _dynamoDbClient;
    private readonly IAmazonSQS _sqsClient;

    // Constructor with AI service interface (supports Bedrock, Gemini, etc.)
    public TemplateService(
        ITemplateRepository templateRepository,
        IAITemplateService aiService,
        ILambdaContext context,
        IAmazonDynamoDB? dynamoDbClient = null,
        IAmazonSQS? sqsClient = null)
    {
        _templateRepository = templateRepository;
        _aiService = aiService;
        _context = context;
        _dynamoDbClient = dynamoDbClient;
        _sqsClient = sqsClient ?? new AmazonSQSClient(Amazon.RegionEndpoint.APSouth1);
    }

    public async Task<List<TemplateDto>> GetTemplatesAsync(string userId)
    {
        // Get user's own templates
        var userTemplates = await _templateRepository.GetByUserIdAsync(userId);
        
        // Get global templates (available to all users)
        var globalTemplates = await _templateRepository.GetByUserIdAsync("GLOBAL");
        
        // Combine and return (global templates first, then user templates)
        var allTemplates = globalTemplates.Concat(userTemplates).ToList();
        
        return allTemplates.Select(MapToDto).ToList();
    }

    public async Task<TemplateDto?> GetTemplateAsync(string templateId, string userId)
    {
        var template = await _templateRepository.GetByIdAsync(templateId);
        
        // Verify ownership: must be either user's template OR global template
        if (template == null || (template.UserId != userId && template.UserId != "GLOBAL"))
            return null;
            
        return MapToDto(template);
    }

    public async Task<TemplateDto> CreateTemplateAsync(CreateTemplateRequest request, string userId)
    {
        var jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
        };

        var template = new Models.Template
        {
            UserId = userId,
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            Category = request.Category,
            RulesJson = JsonConvert.SerializeObject(request.Rules, jsonSettings),
            Status = "ACTIVE",
            IsStepwise = false // Manual creation is traditional mode
        };

        var createdTemplate = await _templateRepository.CreateAsync(template);
        return MapToDto(createdTemplate);
    }

    public async Task<TemplateDto> UpdateTemplateAsync(string templateId, CreateTemplateRequest request, string userId)
    {
        var template = await _templateRepository.GetByIdAsync(templateId);
        
        // Prevent updating global templates or templates belonging to other users
        if (template == null || template.UserId != userId)
            throw new UnauthorizedAccessException("Template not found or access denied");
        
        // Cannot update global templates
        if (template.UserId == "GLOBAL")
            throw new UnauthorizedAccessException("Cannot modify global templates");

        var jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
        };

        template.Name = request.Name;
        template.Description = request.Description ?? string.Empty;
        template.Category = request.Category;
        template.RulesJson = JsonConvert.SerializeObject(request.Rules, jsonSettings);

        var updatedTemplate = await _templateRepository.UpdateAsync(template);
        return MapToDto(updatedTemplate);
    }

    public async Task DeleteTemplateAsync(string templateId, string userId)
    {
        var template = await _templateRepository.GetByIdAsync(templateId);
        
        // Prevent deleting global templates or templates belonging to other users
        if (template == null || template.UserId != userId)
            throw new UnauthorizedAccessException("Template not found or access denied");
        
        // Cannot delete global templates
        if (template.UserId == "GLOBAL")
            throw new UnauthorizedAccessException("Cannot delete global templates");

        // Delete the template
        await _templateRepository.DeleteAsync(templateId);
        
        // If this template was linked to a conversation, clear the conversation's TemplateId
        if (!string.IsNullOrEmpty(template.ConversationId) && _dynamoDbClient != null)
        {
            try
            {
                var updateRequest = new UpdateItemRequest
                {
                    TableName = "Conversations",
                    Key = new Dictionary<string, AttributeValue>
                    {
                        { "ConversationId", new AttributeValue { S = template.ConversationId } }
                    },
                    UpdateExpression = "REMOVE TemplateId SET UpdatedAt = :updatedAt",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        { ":updatedAt", new AttributeValue { S = DateTime.UtcNow.ToString("o") } }
                    }
                };

                await _dynamoDbClient.UpdateItemAsync(updateRequest);
                _context.Logger.LogInformation($"Cleared TemplateId from conversation {template.ConversationId}");
            }
            catch (Exception ex)
            {
                _context.Logger.LogWarning($"Failed to clear TemplateId from conversation {template.ConversationId}: {ex.Message}");
                // Don't throw - template deletion succeeded, this is cleanup
            }
        }
    }

    public async Task<TemplateDto> GenerateTemplateAsync(GenerateTemplateRequest request, string userId)
    {
        if (_dynamoDbClient == null)
        {
            throw new InvalidOperationException("DynamoDB client not initialized. Template generation requires conversation access.");
        }

        _context.Logger.LogInformation($"Queueing template generation from conversation {request.ConversationId}");

        // 1. Fetch conversation from DynamoDB
        var conversation = await GetConversationAsync(request.ConversationId.ToString());
        if (conversation == null)
        {
            throw new ArgumentException($"Conversation {request.ConversationId} not found");
        }

        // Verify ownership
        if (conversation.UserId != userId)
        {
            throw new UnauthorizedAccessException("You don't have access to this conversation");
        }

        // 2. Send SQS message for async processing (TemplateGenerationEngine will create the template)
        await SendTemplateGenerationMessageAsync(request, userId);
        _context.Logger.LogInformation($"SQS message sent for conversation {request.ConversationId}");

        // 3. Return a response indicating the request is queued
        return new TemplateDto
        {
            Id = Guid.Empty, // Template will be created by TemplateGenerationEngine
            Name = request.Name,
            Description = request.Description,
            Category = request.Category,
            ConversationId = request.ConversationId.ToString(),
            Status = "QUEUED", // Indicates request is queued for processing
            IsStepwise = true,
            TemplateType = request.TemplateType,
            Direction = request.Direction,
            Timeframe = request.Timeframe,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Send SQS message to trigger async template generation
    /// </summary>
    private async Task SendTemplateGenerationMessageAsync(GenerateTemplateRequest request, string userId)
    {
        var queueUrl = await GetQueueUrlAsync("template-generation-queue");
        
        // Fetch messages from conversation
        var messages = await GetMessagesByConversationIdAsync(request.ConversationId.ToString());
        if (messages.Count == 0)
        {
            throw new ArgumentException("Conversation has no messages");
        }

        // Build message payload
        var messagePayload = new
        {
            ConversationId = request.ConversationId.ToString(),
            UserId = userId,
            Name = request.Name,
            Description = request.Description,
            Category = request.Category,
            TemplateType = request.TemplateType,
            Direction = request.Direction,
            Timeframe = request.Timeframe,
            Messages = messages.Select(m => new
            {
                Role = m.Role,
                Content = m.Content,
                Timestamp = m.Timestamp
            }).ToList()
        };

        var messageBody = JsonConvert.SerializeObject(messagePayload);
        
        var sendMessageRequest = new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = messageBody,
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                {
                    "ConversationId",
                    new MessageAttributeValue { DataType = "String", StringValue = request.ConversationId.ToString() }
                },
                {
                    "UserId",
                    new MessageAttributeValue { DataType = "String", StringValue = userId }
                }
            }
        };

        await _sqsClient.SendMessageAsync(sendMessageRequest);
        _context.Logger.LogInformation($"SQS message sent for conversation {request.ConversationId}");
    }

    /// <summary>
    /// Get SQS queue URL by name
    /// </summary>
    private async Task<string> GetQueueUrlAsync(string queueName)
    {
        try
        {
            var response = await _sqsClient.GetQueueUrlAsync(queueName);
            return response.QueueUrl;
        }
        catch (Exception ex)
        {
            _context.Logger.LogError($"Failed to get queue URL for {queueName}: {ex.Message}");
            throw new Exception($"SQS queue '{queueName}' not found. Please ensure it exists in the same region.");
        }
    }

    /// <summary>
    /// Process template generation asynchronously (runs in background)
    /// NOTE: This method is now deprecated - processing moved to TemplateGenerationEngine Lambda
    /// </summary>
    private async Task ProcessTemplateGenerationAsync(string templateId, GenerateTemplateRequest request, string userId)
    {
        _context.Logger.LogInformation($"[ASYNC] Processing template generation for {templateId}");

        // 1. Fetch messages from conversation
        var messages = await GetMessagesByConversationIdAsync(request.ConversationId.ToString());
        if (messages.Count == 0)
        {
            throw new ArgumentException("Conversation has no messages");
        }

        // 2. Build conversation summary for AI
        var conversationSummary = BuildConversationSummary(messages);
        _context.Logger.LogInformation($"[ASYNC] Built conversation summary: {conversationSummary.Length} characters");

        // 3. Generate stepwise template using AI service
        string templateJson;
        try
        {
            templateJson = await _aiService.GenerateStepwiseTemplateAsync(
                conversationSummary,
                request.Name,
                request.Description,
                request.Category
            );

            _context.Logger.LogInformation($"[ASYNC] AI generated template: {templateJson.Substring(0, Math.Min(200, templateJson.Length))}...");
        }
        catch (Exception ex)
        {
            _context.Logger.LogError($"[ASYNC] AI service error: {ex.Message}");
            throw new Exception($"Failed to generate template: {ex.Message}");
        }

        // 4. Parse and validate the generated template
        StepwiseTemplateRules? stepwiseRules;
        try
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
                {
                    NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy()
                }
            };
            stepwiseRules = JsonConvert.DeserializeObject<StepwiseTemplateRules>(templateJson, settings);

            if (stepwiseRules == null)
            {
                throw new Exception("Failed to parse AI response into valid template structure");
            }
        }
        catch (Exception ex)
        {
            _context.Logger.LogError($"[ASYNC] Template validation error: {ex.Message}\nJSON: {templateJson}");
            throw new Exception($"Generated template is invalid: {ex.Message}");
        }

        // 5. Update template with generated rules and set status to ACTIVE
        await UpdateTemplateWithRulesAsync(templateId, templateJson, "ACTIVE");
        _context.Logger.LogInformation($"[ASYNC] Template {templateId} updated with generated rules, status: ACTIVE");

        // 6. Update conversation status and link template
        await UpdateConversationAsync(request.ConversationId.ToString(), templateId, "completed");
        _context.Logger.LogInformation($"[ASYNC] Conversation {request.ConversationId} updated with template ID");
    }

    /// <summary>
    /// Update template with generated rules JSON
    /// </summary>
    private async Task UpdateTemplateWithRulesAsync(string templateId, string rulesJson, string status)
    {
        var updateRequest = new UpdateItemRequest
        {
            TableName = "Templates",
            Key = new Dictionary<string, AttributeValue>
            {
                { "TemplateId", new AttributeValue { S = templateId } }
            },
            UpdateExpression = "SET RulesJson = :rulesJson, #status = :status, UpdatedAt = :updatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                { "#status", "Status" }
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                { ":rulesJson", new AttributeValue { S = rulesJson } },
                { ":status", new AttributeValue { S = status } },
                { ":updatedAt", new AttributeValue { S = DateTime.UtcNow.ToString("o") } }
            }
        };

        await _dynamoDbClient!.UpdateItemAsync(updateRequest);
    }

    /// <summary>
    /// Update template status (e.g., to FAILED with error message)
    /// </summary>
    private async Task UpdateTemplateStatusAsync(string templateId, string status, string? errorMessage = null)
    {
        var updateExpression = "SET #status = :status, UpdatedAt = :updatedAt";
        var expressionValues = new Dictionary<string, AttributeValue>
        {
            { ":status", new AttributeValue { S = status } },
            { ":updatedAt", new AttributeValue { S = DateTime.UtcNow.ToString("o") } }
        };

        if (!string.IsNullOrEmpty(errorMessage))
        {
            updateExpression += ", ErrorMessage = :errorMessage";
            expressionValues[":errorMessage"] = new AttributeValue { S = errorMessage };
        }

        var updateRequest = new UpdateItemRequest
        {
            TableName = "Templates",
            Key = new Dictionary<string, AttributeValue>
            {
                { "TemplateId", new AttributeValue { S = templateId } }
            },
            UpdateExpression = updateExpression,
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                { "#status", "Status" }
            },
            ExpressionAttributeValues = expressionValues
        };

        await _dynamoDbClient!.UpdateItemAsync(updateRequest);
    }

    // Helper method to fetch conversation from DynamoDB
    private async Task<ChatConversation?> GetConversationAsync(string conversationId)
    {
        var request = new GetItemRequest
        {
            TableName = "Conversations",
            Key = new Dictionary<string, AttributeValue>
            {
                { "ConversationId", new AttributeValue { S = conversationId } }
            }
        };

        var response = await _dynamoDbClient!.GetItemAsync(request);
        
        if (!response.IsItemSet)
            return null;

        return new ChatConversation
        {
            ConversationId = response.Item["ConversationId"].S,
            UserId = response.Item["UserId"].S,
            Title = response.Item.ContainsKey("Title") ? response.Item["Title"].S : "Untitled",
            Status = response.Item.ContainsKey("Status") ? response.Item["Status"].S : "active",
            TemplateId = response.Item.ContainsKey("TemplateId") ? response.Item["TemplateId"].S : null
        };
    }

    // Helper method to fetch messages by conversation ID
    private async Task<List<ChatMessage>> GetMessagesByConversationIdAsync(string conversationId)
    {
        var request = new QueryRequest
        {
            TableName = "Messages",
            IndexName = "ConversationIdIndex",
            KeyConditionExpression = "ConversationId = :conversationId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                { ":conversationId", new AttributeValue { S = conversationId } }
            }
        };

        var response = await _dynamoDbClient!.QueryAsync(request);
        
        return response.Items
            .Select(item => new ChatMessage
            {
                MessageId = item["MessageId"].S,
                ConversationId = item["ConversationId"].S,
                Role = item["Role"].S,
                Content = item["Content"].S,
                Timestamp = DateTime.Parse(item["Timestamp"].S)
            })
            .OrderBy(m => m.Timestamp)
            .ToList();
    }

    // Helper method to update conversation
    private async Task UpdateConversationAsync(string conversationId, string templateId, string status)
    {
        var request = new UpdateItemRequest
        {
            TableName = "Conversations",
            Key = new Dictionary<string, AttributeValue>
            {
                { "ConversationId", new AttributeValue { S = conversationId } }
            },
            UpdateExpression = "SET #status = :status, TemplateId = :templateId, UpdatedAt = :updatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                { "#status", "Status" }
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                { ":status", new AttributeValue { S = status } },
                { ":templateId", new AttributeValue { S = templateId } },
                { ":updatedAt", new AttributeValue { S = DateTime.UtcNow.ToString("o") } }
            }
        };

        await _dynamoDbClient!.UpdateItemAsync(request);
    }

    // Helper method to build conversation summary
    private string BuildConversationSummary(List<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== CONVERSATION HISTORY ===");
        sb.AppendLine();

        foreach (var message in messages)
        {
            sb.AppendLine($"{message.Role.ToUpper()}: {message.Content}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // Simple POCO classes for Chat data (avoiding external dependencies)
    private class ChatConversation
    {
        public string ConversationId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? TemplateId { get; set; }
    }

    private class ChatMessage
    {
        public string MessageId { get; set; } = string.Empty;
        public string ConversationId { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    private TemplateDto MapToDto(Models.Template template)
    {
        // Parse RulesJson to extract stepwise components if needed
        StepwiseTemplateRules? stepwiseRules = null;
        string? longEntryStepsJson = null;
        string? longExitStepsJson = null;
        string? shortEntryStepsJson = null;
        string? shortExitStepsJson = null;

        if (template.IsStepwise && !string.IsNullOrEmpty(template.RulesJson))
        {
            try
            {
                var settings = new JsonSerializerSettings
                {
                    ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
                    {
                        NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy()
                    }
                };
                
                stepwiseRules = JsonConvert.DeserializeObject<StepwiseTemplateRules>(template.RulesJson, settings);
                
                if (stepwiseRules != null)
                {
                    var camelCaseSettings = new JsonSerializerSettings
                    {
                        ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
                    };
                    
                    longEntryStepsJson = JsonConvert.SerializeObject(stepwiseRules.LongEntrySteps, camelCaseSettings);
                    longExitStepsJson = JsonConvert.SerializeObject(stepwiseRules.LongExitSteps, camelCaseSettings);
                    shortEntryStepsJson = JsonConvert.SerializeObject(stepwiseRules.ShortEntrySteps, camelCaseSettings);
                    shortExitStepsJson = JsonConvert.SerializeObject(stepwiseRules.ShortExitSteps, camelCaseSettings);
                }
            }
            catch (Exception ex)
            {
                _context.Logger.LogError($"Error parsing stepwise rules: {ex.Message}");
            }
        }

        return new TemplateDto
        {
            Id = Guid.Parse(template.TemplateId),
            Name = template.Name,
            Description = template.Description ?? string.Empty,
            Category = template.Category,
            Status = template.Status,
            IsStepwise = template.IsStepwise,
            RulesJson = template.RulesJson,
            LongEntryStepsJson = longEntryStepsJson,
            LongExitStepsJson = longExitStepsJson,
            ShortEntryStepsJson = shortEntryStepsJson,
            ShortExitStepsJson = shortExitStepsJson,
            ErrorMessage = template.ErrorMessage,
            ConversationId = template.ConversationId,
            TemplateType = template.TemplateType,
            RulesJsonSignal = template.RulesJsonSignal,
            Direction = template.Direction,
            Timeframe = template.Timeframe,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        };
    }
}

