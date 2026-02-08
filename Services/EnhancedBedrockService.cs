using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Lambda.Core;
using AlgoXera.Lambda.TemplateEngine.Models;
using AlgoXera.Lambda.TemplateEngine.Repositories;
using Newtonsoft.Json;

namespace AlgoXera.Lambda.TemplateEngine.Services;

/// <summary>
/// Enhanced Bedrock service with decoupled indicator prompts.
/// Uses a two-phase approach:
/// 1. Extract indicators from conversation (lightweight)
/// 2. Build final prompt with only relevant indicator definitions
/// </summary>
public class EnhancedBedrockService : IAITemplateService
{
    private readonly AmazonBedrockRuntimeClient _bedrockClient;
    private readonly IIndicatorRepository _indicatorRepository;
    private readonly ILambdaContext? _context;
    private readonly string _promptArn;
    private readonly string _extractionModelId;

    // Default model for indicator extraction (lightweight, fast)
    private const string DEFAULT_EXTRACTION_MODEL = "anthropic.claude-3-haiku-20240307-v1:0";

    public EnhancedBedrockService(
        IIndicatorRepository indicatorRepository,
        ILambdaContext? context = null,
        string? promptArn = null,
        string? extractionModelId = null)
    {
        _bedrockClient = new AmazonBedrockRuntimeClient(Amazon.RegionEndpoint.APSouth1);
        _indicatorRepository = indicatorRepository;
        _context = context;
        _promptArn = promptArn ?? Environment.GetEnvironmentVariable("BEDROCK_PROMPT_ARN") 
            ?? "arn:aws:bedrock:ap-south-1:428021717924:prompt/WD76YPY3TY";
        _extractionModelId = extractionModelId ?? Environment.GetEnvironmentVariable("BEDROCK_EXTRACTION_MODEL") 
            ?? DEFAULT_EXTRACTION_MODEL;
    }

    /// <summary>
    /// Generate a stepwise template using the two-phase approach
    /// </summary>
    public async Task<string> GenerateStepwiseTemplateAsync(
        string conversationSummary,
        string templateName,
        string templateDescription,
        string templateCategory)
    {
        _context?.Logger.LogInformation("Starting two-phase template generation");

        // Phase 1: Extract indicators from conversation
        var extractedIndicators = await ExtractIndicatorsFromConversationAsync(conversationSummary);
        _context?.Logger.LogInformation($"Phase 1 complete: Extracted {extractedIndicators.Count} indicators: {string.Join(", ", extractedIndicators)}");

        // Phase 2: Fetch indicator definitions from DynamoDB
        var indicatorDefinitions = await _indicatorRepository.GetByTypesAsync(extractedIndicators);
        _context?.Logger.LogInformation($"Phase 2 complete: Fetched {indicatorDefinitions.Count} indicator definitions");

        // If no indicators found in DB, use defaults
        if (!indicatorDefinitions.Any())
        {
            _context?.Logger.LogWarning("No indicator definitions found in DB, using fallback defaults");
            indicatorDefinitions = GetFallbackIndicatorDefinitions(extractedIndicators);
        }

        // Phase 3: Build optimized prompt with only relevant indicators
        var templateJson = await GenerateTemplateWithIndicatorsAsync(
            conversationSummary,
            templateName,
            templateDescription,
            templateCategory,
            indicatorDefinitions
        );

        return templateJson;
    }

    /// <summary>
    /// Phase 1: Extract indicator types mentioned in the conversation
    /// Uses a lightweight model for fast extraction
    /// </summary>
    private async Task<List<string>> ExtractIndicatorsFromConversationAsync(string conversationSummary)
    {
        var extractionPrompt = $@"You are analyzing a conversation about creating a trading strategy. The conversation may contain suggested indicators and final selected indicators.

CONVERSATION:
{conversationSummary}

QUESTION: What are the FINAL indicators that were selected/chosen for this strategy? 

Ignore any lists of suggested indicators. Only return the indicators that were actually selected/chosen by the user for their final strategy.

Return your answer as a JSON array using ONLY these standard type names:
- rsi (Relative Strength Index) - also known as RSI
- ema (Exponential Moving Average) - also known as EMA
- sma (Simple Moving Average) - also known as SMA
- macd (Moving Average Convergence Divergence) - also known as MACD
- bollingerbands (Bollinger Bands) - also known as BBANDS, BB
- atr (Average True Range) - also known as ATR
- adx (Average Directional Index) - also known as ADX
- stochastic (Stochastic Oscillator) - also known as STOCH
- supertrend (Supertrend)
- cci (Commodity Channel Index) - also known as CCI
- williamsr (Williams %R)
- mfi (Money Flow Index) - also known as MFI
- obv (On-Balance Volume) - also known as OBV
- vwap (Volume Weighted Average Price) - also known as VWAP
- ichimoku (Ichimoku Cloud)
- parabolicsar (Parabolic SAR) - also known as PSAR
- roc (Rate of Change) - also known as ROC
- momentum (Momentum)

CRITICAL RULES:
1. Return ONLY a JSON array of strings
2. Use lowercase indicator type names
3. Extract ONLY indicators that are EXPLICITLY mentioned by name in the conversation
4. DO NOT infer or add related indicators (e.g., if Bollinger Bands is mentioned, do NOT add SMA or EMA)
5. DO NOT add indicators that might be used internally by other indicators
6. If the same indicator is mentioned with different periods, return just one entry
7. Be very strict - only extract what is directly mentioned

Example:
- If conversation says ""RSI and Bollinger Bands"", return: [""rsi"", ""bollingerbands""]
- If conversation says ""MACD crossover"", return: [""macd""]
- If conversation says ""EMA 20 and EMA 50"", return: [""ema""]

Return ONLY the JSON array with NO explanation, NO markdown, NO additional text.
Your entire response should be just the array: [""indicator1"", ""indicator2""]";

        var messages = new List<Message>
        {
            new Message
            {
                Role = ConversationRole.User,
                Content = new List<ContentBlock>
                {
                    new ContentBlock { Text = extractionPrompt }
                }
            }
        };

        var request = new ConverseRequest
        {
            ModelId = _extractionModelId,
            Messages = messages,
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = 500, // Increased to prevent truncation
                Temperature = 0.0f // Deterministic for extraction
            }
        };

        try
        {
            var response = await _bedrockClient.ConverseAsync(request);
            var responseText = response?.Output?.Message?.Content?[0]?.Text ?? "[]";
            
            _context?.Logger.LogInformation($"Raw AI response for indicator extraction: {responseText}");

            // Clean up response
            responseText = responseText.Trim();
            if (responseText.StartsWith("```"))
            {
                responseText = responseText.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("```")).Aggregate((a, b) => a + b);
                responseText = responseText.Trim();
            }
            
            // Try to extract JSON array from response if it contains other text
            if (!responseText.StartsWith("["))
            {
                // Look for a JSON array in the response
                var startIndex = responseText.IndexOf('[');
                var endIndex = responseText.LastIndexOf(']');
                
                if (startIndex >= 0 && endIndex > startIndex)
                {
                    responseText = responseText.Substring(startIndex, endIndex - startIndex + 1);
                    _context?.Logger.LogInformation($"Extracted JSON array from response: {responseText}");
                }
                else
                {
                    _context?.Logger.LogWarning($"Could not find JSON array in response: {responseText.Substring(0, Math.Min(100, responseText.Length))}");
                    return new List<string> { "price" };
                }
            }

            var indicators = JsonConvert.DeserializeObject<List<string>>(responseText) ?? new List<string>();
            
            // Remove duplicates and normalize
            indicators = indicators.Select(i => i.ToLowerInvariant()).Distinct().ToList();
            
            // Always ensure 'price' is included for strategies
            if (!indicators.Contains("price"))
            {
                indicators.Add("price");
            }

            return indicators;
        }
        catch (Exception ex)
        {
            _context?.Logger.LogError($"Indicator extraction failed: {ex.Message}");
            // Return minimal default on failure
            return new List<string> { "price" };
        }
    }

    /// <summary>
    /// Phase 3: Generate template with only the relevant indicator definitions
    /// </summary>
    private async Task<string> GenerateTemplateWithIndicatorsAsync(
        string conversationSummary,
        string templateName,
        string templateDescription,
        string templateCategory,
        List<IndicatorDefinition> indicators)
    {
        // Build indicator-specific prompt sections
        var indicatorPromptSection = BuildIndicatorPromptSection(indicators);

        var promptMessage = $@"CONVERSATION TO ANALYZE:
{conversationSummary}

STEPWISE TEMPLATE TO CREATE:
Name: ""{templateName}""
Description: ""{templateDescription}""
Category: ""{templateCategory}""

Generate a STEPWISE trading strategy with SEQUENTIAL steps (T1→T2→T3). Each step must complete before the next.

CRITICAL REQUIREMENTS:
1. Return ONLY pure JSON - NO markdown, NO code blocks, NO explanations
2. Use SEQUENTIAL STEPS (stepOrder: 1, 2, 3) with stepName format ""T1: Description""
3. ALL IDs (both indicator IDs and condition IDs) MUST be UUIDs in format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx (e.g., ""89018901-6789-4bcd-ef01-234567890123"")
4. Indicator parameters must be DEFINITIONS with type/label/min/max/defaultValue
5. Only use the indicators defined below - do NOT invent new ones

AVAILABLE INDICATORS (use ONLY these - but generate new UUIDs for IDs):
{indicatorPromptSection}

UUID FORMAT REQUIREMENTS (CRITICAL):
- ALL indicator IDs MUST be UUIDs like ""89018901-6789-4bcd-ef01-234567890123""
- ALL condition IDs MUST be UUIDs like ""a1b2c3d4-e5f6-7890-abcd-ef1234567890""
- Generate unique random UUIDs for each indicator and condition
- In conditions, reference indicator UUIDs you defined in the indicators array
- DO NOT use descriptive IDs like ""rsi_14"" or ""supertrend_10_3"" - ONLY UUIDs

CONDITION TYPES:
1. 'above' or 'below': Compare indicator to threshold
   - Set: indicator, value
   - Null: indicator1, indicator2

2. 'crossover' or 'crossunder': Compare two indicators
   - Set: indicator1, indicator2
   - Null: indicator, value

JSON SCHEMA:
{{
  ""name"": ""{templateName}"",
  ""description"": ""{templateDescription}"",
  ""version"": ""1.0"",
  ""category"": ""{templateCategory}"",
  ""indicators"": [
    {{
      ""id"": ""89018901-6789-4bcd-ef01-234567890123"",
      ""type"": ""INDICATOR_TYPE"",
      ""label"": ""Display Name"",
      ""parameters"": {{ ... }}
    }}
  ],
  ""LongEntrySteps"": [
    {{
      ""stepOrder"": 1,
      ""stepName"": ""T1: Entry Condition"",
      ""description"": ""Description of what triggers entry"",
      ""conditions"": [
        {{
          ""id"": ""a1b2c3d4-e5f6-7890-abcd-ef1234567890"",
          ""type"": ""below|above|crossover|crossunder"",
          ""description"": ""Condition description"",
          ""indicator"": ""uuid-of-indicator-for-above-below-or-null"",
          ""value"": ""threshold"",
          ""indicator1"": ""uuid-of-first-indicator-for-crossover-or-null"",
          ""indicator2"": ""uuid-of-second-indicator-for-crossover-or-null""
        }}
      ],
      ""isMandatory"": true
    }}
  ],
  ""LongExitSteps"": [...],
  ""ShortEntrySteps"": [...],
  ""ShortExitSteps"": [...]
}}

IMPORTANT: Use PascalCase ONLY for step array names (LongEntrySteps, LongExitSteps, ShortEntrySteps, ShortExitSteps). Use camelCase for all other properties.

Return ONLY the JSON, no explanations.";

        var messages = new List<Message>
        {
            new Message
            {
                Role = ConversationRole.User,
                Content = new List<ContentBlock>
                {
                    new ContentBlock { Text = promptMessage }
                }
            }
        };

        var request = new ConverseRequest
        {
            ModelId = _promptArn,
            Messages = messages
        };

        try
        {
            var response = await _bedrockClient.ConverseAsync(request);

            if (response?.Output?.Message?.Content?.Count > 0)
            {
                var responseText = response.Output.Message.Content[0].Text ?? "";
                
                // Extract JSON from response
                responseText = CleanJsonResponse(responseText);
                
                // Validate JSON
                try
                {
                    JsonConvert.DeserializeObject<object>(responseText);
                    return responseText;
                }
                catch
                {
                    throw new InvalidOperationException("Generated response is not valid JSON");
                }
            }

            throw new InvalidOperationException("No response generated from Bedrock");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new HttpRequestException($"Bedrock API error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Build the indicator-specific section of the prompt from definitions
    /// </summary>
    private string BuildIndicatorPromptSection(List<IndicatorDefinition> indicators)
    {
        var sections = new List<string>();

        foreach (var indicator in indicators.OrderBy(i => i.SortOrder))
        {
            if (!string.IsNullOrWhiteSpace(indicator.PromptSnippet))
            {
                sections.Add(indicator.PromptSnippet);
            }
            else
            {
                // Build from structured data if no prompt snippet
                var section = $@"- {indicator.DisplayName.ToUpperInvariant()}: 
    id=""{indicator.ExampleId}"", type=""{indicator.IndicatorType.ToUpperInvariant()}""
    parameters={indicator.ParametersJson}";
                sections.Add(section);
            }
        }

        return string.Join("\n\n", sections);
    }

    /// <summary>
    /// Clean markdown wrappers from JSON response
    /// </summary>
    private string CleanJsonResponse(string responseText)
    {
        responseText = responseText.Trim();
        
        if (responseText.StartsWith("```json"))
        {
            responseText = responseText.Substring(7);
        }
        else if (responseText.StartsWith("```"))
        {
            responseText = responseText.Substring(3);
        }
        
        if (responseText.EndsWith("```"))
        {
            responseText = responseText.Substring(0, responseText.Length - 3);
        }
        
        return responseText.Trim();
    }

    /// <summary>
    /// Fallback indicator definitions when DB is empty
    /// </summary>
    private List<IndicatorDefinition> GetFallbackIndicatorDefinitions(List<string> types)
    {
        var fallbacks = new Dictionary<string, IndicatorDefinition>
        {
            ["rsi"] = new IndicatorDefinition
            {
                IndicatorType = "rsi",
                DisplayName = "RSI",
                ExampleId = "rsi-indicator-uuid",
                PromptSnippet = @"- RSI (Relative Strength Index):
    type=""RSI"", Generate a UUID for id (e.g., ""f1a2b3c4-d5e6-7890-abcd-111111111111"")
    parameters={""period"": {""type"": ""number"", ""label"": ""Period"", ""min"": 2, ""max"": 50, ""defaultValue"": 14}}"
            },
            ["ema"] = new IndicatorDefinition
            {
                IndicatorType = "ema",
                DisplayName = "EMA",
                ExampleId = "ema-indicator-uuid",
                PromptSnippet = @"- EMA (Exponential Moving Average):
    type=""EMA"", Generate a UUID for id (e.g., ""e2b3c4d5-e6f7-8901-bcde-222222222222"")
    parameters={""period"": {""type"": ""number"", ""label"": ""Period"", ""min"": 1, ""max"": 200, ""defaultValue"": 20}}"
            },
            ["sma"] = new IndicatorDefinition
            {
                IndicatorType = "sma",
                DisplayName = "SMA",
                ExampleId = "sma-indicator-uuid",
                PromptSnippet = @"- SMA (Simple Moving Average):
    type=""SMA"", Generate a UUID for id (e.g., ""s3c4d5e6-f7a8-9012-cdef-333333333333"")
    parameters={""period"": {""type"": ""number"", ""label"": ""Period"", ""min"": 1, ""max"": 200, ""defaultValue"": 50}}"
            },
            ["macd"] = new IndicatorDefinition
            {
                IndicatorType = "macd",
                DisplayName = "MACD",
                ExampleId = "macd-indicator-uuid",
                PromptSnippet = @"- MACD (Moving Average Convergence Divergence):
    type=""MACD"", Generate a UUID for id (e.g., ""m4d5e6f7-a8b9-0123-def0-444444444444"")
    For signal line, generate a separate indicator with type=""MACD_SIGNAL"" and its own UUID
    parameters={""fastPeriod"": {""type"": ""number"", ""min"": 5, ""max"": 50, ""defaultValue"": 12}, ""slowPeriod"": {""type"": ""number"", ""min"": 10, ""max"": 100, ""defaultValue"": 26}, ""signalPeriod"": {""type"": ""number"", ""min"": 5, ""max"": 50, ""defaultValue"": 9}}"
            },
            ["bollingerbands"] = new IndicatorDefinition
            {
                IndicatorType = "bollingerbands",
                DisplayName = "Bollinger Bands",
                ExampleId = "bb-indicator-uuid",
                PromptSnippet = @"- BOLLINGER BANDS:
    type=""BOLLINGERBANDS"", Generate a UUID for id (e.g., ""b5e6f7a8-b9c0-1234-ef01-555555555555"")
    For upper/middle/lower bands, use type=""BB_UPPER"", ""BB_MIDDLE"", ""BB_LOWER"" with their own UUIDs
    parameters={""period"": {""type"": ""number"", ""min"": 5, ""max"": 100, ""defaultValue"": 20}, ""standardDeviations"": {""type"": ""number"", ""min"": 1, ""max"": 5, ""defaultValue"": 2}}"
            },
            ["atr"] = new IndicatorDefinition
            {
                IndicatorType = "atr",
                DisplayName = "ATR",
                ExampleId = "atr-indicator-uuid",
                PromptSnippet = @"- ATR (Average True Range):
    type=""ATR"", Generate a UUID for id (e.g., ""a6f7a8b9-c0d1-2345-f012-666666666666"")
    parameters={""period"": {""type"": ""number"", ""label"": ""Period"", ""min"": 5, ""max"": 50, ""defaultValue"": 14}}"
            },
            ["adx"] = new IndicatorDefinition
            {
                IndicatorType = "adx",
                DisplayName = "ADX",
                ExampleId = "adx-indicator-uuid",
                PromptSnippet = @"- ADX (Average Directional Index):
    type=""ADX"", Generate a UUID for id (e.g., ""d7a8b9c0-d1e2-3456-0123-777777777777"")
    parameters={""period"": {""type"": ""number"", ""label"": ""Period"", ""min"": 5, ""max"": 50, ""defaultValue"": 14}}"
            },
            ["stochastic"] = new IndicatorDefinition
            {
                IndicatorType = "stochastic",
                DisplayName = "Stochastic",
                ExampleId = "stoch-indicator-uuid",
                PromptSnippet = @"- STOCHASTIC:
    type=""STOCHASTIC"", Generate a UUID for id (e.g., ""s8b9c0d1-e2f3-4567-1234-888888888888"")
    parameters={""kPeriod"": {""type"": ""number"", ""min"": 5, ""max"": 50, ""defaultValue"": 14}, ""dPeriod"": {""type"": ""number"", ""min"": 1, ""max"": 10, ""defaultValue"": 3}}"
            },
            ["supertrend"] = new IndicatorDefinition
            {
                IndicatorType = "supertrend",
                DisplayName = "Supertrend",
                ExampleId = "supertrend-indicator-uuid",
                PromptSnippet = @"- SUPERTREND:
    type=""SUPERTREND"", Generate a UUID for id (e.g., ""t9c0d1e2-f3a4-5678-2345-999999999999"")
    parameters={""period"": {""type"": ""number"", ""min"": 5, ""max"": 50, ""defaultValue"": 10}, ""multiplier"": {""type"": ""number"", ""min"": 1, ""max"": 10, ""defaultValue"": 3}}"
            },
            ["price"] = new IndicatorDefinition
            {
                IndicatorType = "price",
                DisplayName = "Price",
                ExampleId = "price-indicator-uuid",
                PromptSnippet = @"- PRICE (for price comparisons):
    type=""PRICE"", Generate a UUID for id (e.g., ""c7e8f9a0-b1c2-3456-0123-222222222222"")
    Use ""priceType"": ""close"" (or ""open"", ""high"", ""low"") in parameters
    parameters={""priceType"": {""type"": ""string"", ""options"": [""close"", ""open"", ""high"", ""low""], ""defaultValue"": ""close""}}"
            }
        };

        return types
            .Where(t => fallbacks.ContainsKey(t.ToLowerInvariant()))
            .Select(t => fallbacks[t.ToLowerInvariant()])
            .ToList();
    }
}

