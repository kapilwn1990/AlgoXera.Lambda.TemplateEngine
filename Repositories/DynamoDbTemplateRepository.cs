using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using AlgoXera.Lambda.TemplateEngine.Models;

namespace AlgoXera.Lambda.TemplateEngine.Repositories;

public class DynamoDbTemplateRepository : ITemplateRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private const string TableName = "Templates";

    public DynamoDbTemplateRepository(IAmazonDynamoDB dynamoDb)
    {
        _dynamoDb = dynamoDb;
    }

    public async Task<Template?> GetByIdAsync(string templateId)
    {
        var request = new GetItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["TemplateId"] = new AttributeValue { S = templateId }
            }
        };

        var response = await _dynamoDb.GetItemAsync(request);
        return response.Item.Count == 0 ? null : MapToTemplate(response.Item);
    }

    public async Task<List<Template>> GetByUserIdAsync(string userId)
    {
        var request = new QueryRequest
        {
            TableName = TableName,
            IndexName = "UserIdIndex",
            KeyConditionExpression = "UserId = :userId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":userId"] = new AttributeValue { S = userId }
            },
            ScanIndexForward = false // Descending order
        };

        var response = await _dynamoDb.QueryAsync(request);
        return response.Items.Select(MapToTemplate).ToList();
    }

    public async Task<Template> CreateAsync(Template template)
    {
        template.TemplateId = Guid.NewGuid().ToString();
        template.CreatedAt = DateTime.UtcNow;
        template.UpdatedAt = DateTime.UtcNow;

        var request = new PutItemRequest
        {
            TableName = TableName,
            Item = MapToAttributeValues(template)
        };

        await _dynamoDb.PutItemAsync(request);
        return template;
    }

    public async Task<Template> UpdateAsync(Template template)
    {
        template.UpdatedAt = DateTime.UtcNow;

        var request = new UpdateItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["TemplateId"] = new AttributeValue { S = template.TemplateId }
            },
            UpdateExpression = "SET #name = :name, Description = :description, Category = :category, #status = :status, IsStepwise = :isStepwise, RulesJson = :rulesJson, ConversationId = :conversationId, UpdatedAt = :updatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#name"] = "Name",
                ["#status"] = "Status"
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":name"] = new AttributeValue { S = template.Name },
                [":description"] = template.Description != null ? new AttributeValue { S = template.Description } : new AttributeValue { NULL = true },
                [":category"] = new AttributeValue { S = template.Category },
                [":status"] = new AttributeValue { S = template.Status },
                [":isStepwise"] = new AttributeValue { BOOL = template.IsStepwise },
                [":rulesJson"] = new AttributeValue { S = template.RulesJson },
                [":conversationId"] = template.ConversationId != null ? new AttributeValue { S = template.ConversationId } : new AttributeValue { NULL = true },
                [":updatedAt"] = new AttributeValue { S = template.UpdatedAt.ToString("O") }
            },
            ReturnValues = ReturnValue.ALL_NEW
        };

        var response = await _dynamoDb.UpdateItemAsync(request);
        return MapToTemplate(response.Attributes);
    }

    public async Task DeleteAsync(string templateId)
    {
        var request = new DeleteItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["TemplateId"] = new AttributeValue { S = templateId }
            }
        };

        await _dynamoDb.DeleteItemAsync(request);
    }

    private Dictionary<string, AttributeValue> MapToAttributeValues(Template template)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["TemplateId"] = new AttributeValue { S = template.TemplateId },
            ["UserId"] = new AttributeValue { S = template.UserId },
            ["Name"] = new AttributeValue { S = template.Name },
            ["Category"] = new AttributeValue { S = template.Category },
            ["Status"] = new AttributeValue { S = template.Status },
            ["IsStepwise"] = new AttributeValue { BOOL = template.IsStepwise },
            ["RulesJson"] = template.RulesJson != null ? new AttributeValue { S = template.RulesJson } : new AttributeValue { NULL = true },
            ["CreatedAt"] = new AttributeValue { S = template.CreatedAt.ToString("O") },
            ["UpdatedAt"] = new AttributeValue { S = template.UpdatedAt.ToString("O") }
        };

        if (template.Description != null)
            item["Description"] = new AttributeValue { S = template.Description };

        if (template.ConversationId != null)
            item["ConversationId"] = new AttributeValue { S = template.ConversationId };
            
        if (template.ErrorMessage != null)
            item["ErrorMessage"] = new AttributeValue { S = template.ErrorMessage };

        return item;
    }

    private Template MapToTemplate(Dictionary<string, AttributeValue> item)
    {
        return new Template
        {
            TemplateId = item["TemplateId"].S,
            UserId = item["UserId"].S,
            Name = item["Name"].S,
            Description = item.ContainsKey("Description") && !item["Description"].NULL ? item["Description"].S : null,
            Category = item["Category"].S,
            Status = item["Status"].S,
            IsStepwise = item["IsStepwise"].BOOL,
            RulesJson = item.ContainsKey("RulesJson") && !item["RulesJson"].NULL ? item["RulesJson"].S : null,
            ConversationId = item.ContainsKey("ConversationId") && !item["ConversationId"].NULL ? item["ConversationId"].S : null,
            ErrorMessage = item.ContainsKey("ErrorMessage") && !item["ErrorMessage"].NULL ? item["ErrorMessage"].S : null,
            TemplateType = item.ContainsKey("TemplateType") && !item["TemplateType"].NULL ? item["TemplateType"].S : "Execution",
            RulesJsonSignal = item.ContainsKey("RulesJsonSignal") && !item["RulesJsonSignal"].NULL ? item["RulesJsonSignal"].S : null,
            Direction = item.ContainsKey("Direction") && !item["Direction"].NULL ? item["Direction"].S : null,
            Timeframe = item.ContainsKey("Timeframe") && !item["Timeframe"].NULL ? item["Timeframe"].S : null,
            CreatedAt = DateTime.Parse(item["CreatedAt"].S),
            UpdatedAt = DateTime.Parse(item["UpdatedAt"].S)
        };
    }
}

