# AlgoXera.Lambda.TemplateEngine

AWS Lambda function for AI-powered trading strategy template generation using multiple AI providers (Google Gemini and AWS Bedrock).

## Features

- **Multi-Provider AI Support**: Supports both Google Gemini and AWS Bedrock (Claude 3.5 Sonnet)
- **Template Generation**: Creates trading strategy templates based on user requirements
- **Stepwise Generation**: Optionally generates templates step-by-step for better control
- **DynamoDB Integration**: Stores generated templates in DynamoDB
- **SQS Integration**: Processes template generation requests from SQS queue
- **Indicator Support**: Integrates with technical indicator definitions

## Environment Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `AI_PROVIDER` | AI provider to use (gemini or bedrock) | `gemini` |
| `GEMINI_API_KEY` | Google Gemini API key | `AIzaSy...` |
| `GEMINI_MODEL` | Gemini model name | `gemini-3-flash-preview` |
| `BEDROCK_PROMPT_ARN` | AWS Bedrock prompt ARN | `arn:aws:bedrock:...` |

## Dependencies

- **AlgoXera.Lambda.Shared**: Shared libraries for common functionality
- **AWS SDK**: DynamoDB, Bedrock Runtime, SQS
- **Newtonsoft.Json**: JSON serialization

## Services

- **TemplateService**: Main service for template generation orchestration
- **GeminiService**: Google Gemini integration for template generation
- **GeminiService_Stepwise**: Stepwise template generation using Gemini
- **BedrockService**: AWS Bedrock integration
- **EnhancedBedrockService**: Enhanced Bedrock service with advanced prompting

## Deployment

This Lambda function is automatically deployed via GitHub Actions when code is pushed to the `main` branch.

**Function Name**: `templateengine-dev`  
**Region**: `us-east-1`  
**Runtime**: .NET 8  
**Memory**: 512 MB  
**Timeout**: 30 seconds

## Local Development

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Run tests
dotnet test

# Publish
dotnet publish --configuration Release --runtime linux-x64 --no-self-contained
```

## API Gateway Integration

This Lambda is invoked via API Gateway for template generation requests and processes background jobs from SQS.

## Related Resources

- DynamoDB Table: `TEMPLATES-DEV`
- SQS Queue: `template-generation-queue`
- IAM Role: `algoxera-lambda-Dev-role`
