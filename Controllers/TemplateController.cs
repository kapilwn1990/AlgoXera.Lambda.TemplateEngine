using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using AlgoXera.Lambda.TemplateEngine.DTOs;
using AlgoXera.Lambda.TemplateEngine.Services;
using AlgoXera.Lambda.SubscriptionShared.Services;
using System.Text.Json;

namespace AlgoXera.Lambda.TemplateEngine.Controllers;

public class TemplateController
{
    private readonly ITemplateService _templateService;
    private readonly ILambdaContext _context;
    private readonly ICreditService _creditService;
    private readonly IFeatureLimitService _featureLimitService;

    public TemplateController(ITemplateService templateService, ILambdaContext context, ICreditService creditService, IFeatureLimitService featureLimitService)
    {
        _templateService = templateService;
        _context = context;
        _creditService = creditService;
        _featureLimitService = featureLimitService;
    }

    public async Task<APIGatewayProxyResponse> HandleGetTemplates(string userId)
    {
        var templates = await _templateService.GetTemplatesAsync(userId);

        // Debug logging for stepwise templates
        foreach (var template in templates.Where(t => t.IsStepwise))
        {
            _context.Logger.LogInformation($"[RESPONSE] Stepwise template: {template.Name}");
            _context.Logger.LogInformation($"  RulesJson length: {template.RulesJson?.Length ?? 0}");
            _context.Logger.LogInformation($"  LongEntryStepsJson length: {template.LongEntryStepsJson?.Length ?? 0}");
        }

        return CreateResponse(200, templates);
    }

    public async Task<APIGatewayProxyResponse> HandleGetTemplate(string templateId, string userId)
    {
        var template = await _templateService.GetTemplateAsync(templateId, userId);
        
        if (template == null)
            return CreateResponse(404, new { message = "Template not found" });

        return CreateResponse(200, template);
    }

    public async Task<APIGatewayProxyResponse> HandleCreateTemplate(string requestBody, string userId)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var createRequest = JsonSerializer.Deserialize<CreateTemplateRequest>(requestBody, options);
        
        if (createRequest == null)
            return CreateResponse(400, new { message = "Invalid request body" });

        var template = await _templateService.CreateTemplateAsync(createRequest, userId);
        return CreateResponse(201, template);
    }

    public async Task<APIGatewayProxyResponse> HandleUpdateTemplate(string templateId, string requestBody, string userId)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var updateRequest = JsonSerializer.Deserialize<CreateTemplateRequest>(requestBody, options);
        
        if (updateRequest == null)
            return CreateResponse(400, new { message = "Invalid request body" });

        try
        {
            var template = await _templateService.UpdateTemplateAsync(templateId, updateRequest, userId);
            return CreateResponse(200, template);
        }
        catch (UnauthorizedAccessException)
        {
            return CreateResponse(404, new { message = "Template not found" });
        }
    }

    public async Task<APIGatewayProxyResponse> HandleDeleteTemplate(string templateId, string userId)
    {
        try
        {
            await _templateService.DeleteTemplateAsync(templateId, userId);
            return CreateResponse(204, null);
        }
        catch (UnauthorizedAccessException)
        {
            return CreateResponse(404, new { message = "Template not found" });
        }
    }

    public async Task<APIGatewayProxyResponse> HandleGenerateTemplate(string requestBody, string userId)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var generateRequest = JsonSerializer.Deserialize<GenerateTemplateRequest>(requestBody, options);
        
        if (generateRequest == null)
            return CreateResponse(400, new { message = "Invalid request body" });

        try
        {
            // 1. CHECK FEATURE LIMIT - TemplateGeneration quota
            var canUse = await _featureLimitService.CanUseFeatureAsync(userId, "TemplateGeneration");
            if (!canUse)
            {
                var limits = await _featureLimitService.GetRemainingLimitsAsync(userId);
                var remaining = limits.GetValueOrDefault("TemplateGeneration", 0);
                _context.Logger.LogWarning($"Template generation limit exceeded: userId={userId}, remaining={remaining}");
                return CreateResponse(403, new { 
                    message = $"Template generation limit exceeded. You have {remaining} template generations remaining this month. Upgrade your tier for more.",
                    errorCode = "FEATURE_LIMIT_EXCEEDED"
                });
            }
            
            // 2. DEDUCT CREDITS - 100 credits for template generation
            var templateId = Guid.NewGuid().ToString();
            var creditsDeducted = await _creditService.DeductCreditsAsync(
                userId,
                "TemplateGeneration",
                templateId,
                $"AI template generation: {generateRequest.Name ?? "Unnamed"}"
            );
            
            if (!creditsDeducted)
            {
                var balance = await _creditService.GetUserBalanceAsync(userId);
                _context.Logger.LogWarning($"Insufficient credits for template generation: userId={userId}, balance={balance}");
                return CreateResponse(402, new { 
                    message = $"Insufficient credits. Template generation requires 100 credits. Available: {balance} credits. Upgrade your tier for more credits.",
                    errorCode = "INSUFFICIENT_CREDITS",
                    required = 100,
                    available = balance
                });
            }
            
            // 3. GENERATE TEMPLATE - Execute AI generation
            _context.Logger.LogInformation($"Generating template: userId={userId}, templateId={templateId}, credits=100");
            var template = await _templateService.GenerateTemplateAsync(generateRequest, userId);
            
            // 4. INCREMENT USAGE - Only on successful generation
            await _featureLimitService.IncrementFeatureUsageAsync(userId, "TemplateGeneration");
            _context.Logger.LogInformation($"Template generated successfully: userId={userId}, templateId={template.Id}");
            
            return CreateResponse(201, template);
        }
        catch (NotImplementedException ex)
        {
            _context.Logger.LogError($"Generate template not implemented: {ex.Message}");
            return CreateResponse(501, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _context.Logger.LogError($"Error generating template: {ex.Message}\n{ex.StackTrace}");
            return CreateResponse(500, new { message = "Failed to generate template", error = ex.Message });
        }
    }

    private APIGatewayProxyResponse CreateResponse(int statusCode, object? body)
    {
        return new APIGatewayProxyResponse
        {
            StatusCode = statusCode,
            Body = body != null ? JsonSerializer.Serialize(body) : string.Empty,
            Headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" },
                { "Access-Control-Allow-Origin", "*" },
                { "Access-Control-Allow-Headers", "Content-Type,Authorization" },
                { "Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS" }
            }
        };
    }
}

