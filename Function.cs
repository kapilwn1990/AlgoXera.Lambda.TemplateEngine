using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using AlgoXera.Lambda.TemplateEngine.Controllers;
using AlgoXera.Lambda.TemplateEngine.Repositories;
using AlgoXera.Lambda.TemplateEngine.Services;
using AlgoXera.Lambda.SubscriptionShared.Services;
using AlgoXera.Lambda.SubscriptionShared.Repositories;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace AlgoXera.Lambda.TemplateEngine;

public class Function
{
    private readonly ICreditService _creditService;
    private readonly IFeatureLimitService _featureLimitService;
    
    public Function()
    {
    }

    public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        try
        {
            context.Logger.LogInformation($"Request: {request.HttpMethod} {request.Path}");

            // Handle CORS preflight requests
            if (request.HttpMethod == "OPTIONS")
            {
                return CreateCorsResponse(200, null);
            }

            // Get userId from authorizer context
            var userId = GetUserIdFromContext(request);
            if (string.IsNullOrEmpty(userId))
            {
                return CreateCorsResponse(401, new { message = "Unauthorized" });
            }

            // Initialize DI with context
            var promptArn = Environment.GetEnvironmentVariable("BEDROCK_PROMPT_ARN") ?? "arn:aws:bedrock:ap-south-1:428021717924:prompt/WD76YPY3TY";
            var dynamoDbClient = new AmazonDynamoDBClient();
            var dynamoDbContext = new DynamoDBContext(dynamoDbClient);
            
            // Initialize subscription services
            var subscriptionRepo = new SubscriptionRepository(dynamoDbContext);
            var transactionRepo = new CreditTransactionRepository(dynamoDbContext);
            var creditCostRepo = new CreditCostRepository(dynamoDbContext);
            var tierConfigRepo = new TierConfigRepository(dynamoDbContext);
            var usageRepo = new FeatureUsageRepository(dynamoDbContext);
            
            var creditService = new CreditService(subscriptionRepo, transactionRepo, creditCostRepo);
            var featureLimitService = new FeatureLimitService(subscriptionRepo, tierConfigRepo, usageRepo);
            
            // Initialize repositories
            var templateRepository = new DynamoDbTemplateRepository(dynamoDbClient);
            var indicatorRepository = new DynamoDbIndicatorRepository(dynamoDbClient);
            
            // Initialize AI service based on provider configuration
            var aiProvider = Environment.GetEnvironmentVariable("AI_PROVIDER")?.ToLower() ?? "gemini";
            context.Logger.LogInformation($"Using AI provider: {aiProvider}");
            
            IAITemplateService aiService;
            if (aiProvider == "bedrock")
            {
                aiService = new EnhancedBedrockService(indicatorRepository, context, promptArn);
            }
            else
            {
                var geminiApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
                var geminiModel = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-2.5-flash-preview-05-20";
                if (string.IsNullOrEmpty(geminiApiKey))
                {
                    context.Logger.LogError("GEMINI_API_KEY is not configured");
                    return CreateCorsResponse(500, new { message = "AI service not configured" });
                }
                aiService = new GeminiService(geminiApiKey, geminiModel, indicatorRepository, context);
            }
            
            // Initialize template service with AI service
            var templateService = new TemplateService(templateRepository, aiService, context, dynamoDbClient);
            var controller = new TemplateController(templateService, context, creditService, featureLimitService);

            return (request.HttpMethod, request.Path) switch
            {
                ("GET", "/api/templates") => await controller.HandleGetTemplates(userId),
                ("GET", var path) when path.StartsWith("/api/templates/") 
                    => await controller.HandleGetTemplate(ExtractTemplateId(path), userId),
                ("POST", "/api/templates") => await controller.HandleCreateTemplate(request.Body, userId),
                ("PUT", var path) when path.StartsWith("/api/templates/") 
                    => await controller.HandleUpdateTemplate(ExtractTemplateId(path), request.Body, userId),
                ("DELETE", var path) when path.StartsWith("/api/templates/") 
                    => await controller.HandleDeleteTemplate(ExtractTemplateId(path), userId),
                ("POST", "/api/templates/generate") => await controller.HandleGenerateTemplate(request.Body, userId),
                _ => CreateCorsResponse(404, new { message = "Not found" })
            };
        }
        catch (UnauthorizedAccessException)
        {
            return CreateCorsResponse(401, new { message = "Unauthorized" });
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error: {ex.Message}\n{ex.StackTrace}");
            return CreateCorsResponse(500, new { message = "Internal server error", error = ex.Message });
        }
    }

    private string ExtractTemplateId(string path)
    {
        return path.Split('/').Last();
    }

    private string GetUserIdFromContext(APIGatewayProxyRequest request)
    {
        try
        {
            // User ID is set by the API Gateway Authorizer in the request context
            if (request.RequestContext?.Authorizer?.ContainsKey("userId") == true)
            {
                return request.RequestContext.Authorizer["userId"]?.ToString() ?? string.Empty;
            }
            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private APIGatewayProxyResponse CreateCorsResponse(int statusCode, object? body)
    {
        return new APIGatewayProxyResponse
        {
            StatusCode = statusCode,
            Body = body != null ? System.Text.Json.JsonSerializer.Serialize(body) : string.Empty,
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

