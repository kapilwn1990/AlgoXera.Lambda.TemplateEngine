using Amazon.Lambda.Core;
using AlgoXera.Lambda.TemplateEngine.Models;
using AlgoXera.Lambda.TemplateEngine.Repositories;
using Newtonsoft.Json;
using System.Text;

namespace AlgoXera.Lambda.TemplateEngine.Services;

/// <summary>
/// Gemini-based template generation service with two-phase approach.
/// Uses the same methodology as EnhancedBedrockService:
/// 1. Extract indicators from conversation (lightweight)
/// 2. Fetch indicator definitions from DynamoDB
/// 3. Build optimized prompt with only relevant indicators
/// </summary>
public class GeminiService : IAITemplateService
{
    private readonly string _apiKey;
    private readonly string _model;
    private readonly HttpClient _httpClient;
    private readonly IIndicatorRepository? _indicatorRepository;
    private readonly ILambdaContext? _context;

    public GeminiService(string apiKey, string model, IIndicatorRepository? indicatorRepository = null, ILambdaContext? context = null)
    {
        _apiKey = apiKey;
        _model = model;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(240) // 4 minute timeout for Gemini API calls (template generation can be slow)
        };
        _indicatorRepository = indicatorRepository;
        _context = context;
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
        _context?.Logger.LogInformation("Starting two-phase Gemini template generation");

        // Phase 1: Extract indicators from conversation
        var extractedIndicators = await ExtractIndicatorsFromConversationAsync(conversationSummary);
        _context?.Logger.LogInformation($"Phase 1 complete: Extracted {extractedIndicators.Count} indicators: {string.Join(", ", extractedIndicators)}");

        // Phase 2: Fetch indicator definitions from DynamoDB
        List<IndicatorDefinition> indicatorDefinitions = new();
        if (_indicatorRepository != null)
        {
            indicatorDefinitions = await _indicatorRepository.GetByTypesAsync(extractedIndicators);
            _context?.Logger.LogInformation($"Phase 2 complete: Fetched {indicatorDefinitions.Count} indicator definitions from DB");
        }

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
    /// Uses a lightweight Gemini call for fast extraction
    /// </summary>
    private async Task<List<string>> ExtractIndicatorsFromConversationAsync(string conversationSummary)
    {
        _context?.Logger.LogInformation($"Conversation summary length: {conversationSummary.Length} characters");
        _context?.Logger.LogInformation($"Conversation preview: {conversationSummary.Substring(0, Math.Min(500, conversationSummary.Length))}");
        
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
- keltner (Keltner Channel)
- donchian (Donchian Channel)
- pivotpoints (Pivot Points)
- zscore (Z-Score)
- prev_high (Previous High) - also known as PREV_HIGH, Previous Day High, Previous Candle High
- prev_low (Previous Low) - also known as PREV_LOW, Previous Day Low, Previous Candle Low
- prev_close (Previous Close) - also known as PREV_CLOSE, Previous Day Close, Previous Candle Close

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

        try
        {
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[] { new { text = extractionPrompt } }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.0, // Deterministic for extraction
                    maxOutputTokens = 500 // Increased to prevent truncation
                }
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                _context?.Logger.LogWarning("Indicator extraction API call failed, using defaults");
                return new List<string> { "rsi", "ema", "sma", "macd", "price" };
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var geminiResponse = JsonConvert.DeserializeObject<GeminiResponse>(responseContent);

            if (geminiResponse?.Candidates == null || geminiResponse.Candidates.Length == 0)
            {
                _context?.Logger.LogWarning("No candidates in Gemini response");
                return new List<string> { "rsi", "ema", "sma", "macd", "price" };
            }

            var responseText = geminiResponse.Candidates[0].Content.Parts[0].Text ?? "[]";
            _context?.Logger.LogInformation($"Raw AI response for indicator extraction: {responseText}");

            // Clean up response - handle markdown code blocks
            responseText = responseText.Trim();
            if (responseText.StartsWith("```"))
            {
                var lines = responseText.Split('\n');
                responseText = string.Join("", lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
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
                    // Fall back to text detection from conversation
                    var detected = DetectIndicatorsFromText(conversationSummary);
                    if (detected.Any())
                    {
                        detected.Add("price");
                        return detected;
                    }
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
            // Don't use DetectIndicatorsFromText as fallback - it's too broad
            // Return minimal safe default instead
            _context?.Logger.LogWarning("Returning safe default indicators due to extraction failure");
            return new List<string> { "price" };
        }
    }

    /// <summary>
    /// Fallback method to detect indicators from text when AI extraction fails
    /// </summary>
    private List<string> DetectIndicatorsFromText(string text)
    {
        var indicators = new List<string>();
        var textLower = text.ToLowerInvariant();

        var indicatorKeywords = new Dictionary<string, string[]>
        {
            ["rsi"] = new[] { "rsi", "relative strength" },
            ["ema"] = new[] { "ema", "exponential moving average" },
            ["sma"] = new[] { "sma", "simple moving average" },
            ["macd"] = new[] { "macd", "moving average convergence" },
            ["stochastic"] = new[] { "stochastic", "stoch", "%k", "%d" },
            ["bollingerbands"] = new[] { "bollinger", "bbands" },
            ["atr"] = new[] { "atr", "average true range" },
            ["adx"] = new[] { "adx", "average directional", "directional index" },
            ["supertrend"] = new[] { "supertrend", "super trend" },
            ["cci"] = new[] { "cci", "commodity channel" },
            ["williamsr"] = new[] { "williams", "williams %r", "williams r" },
            ["mfi"] = new[] { "mfi", "money flow index" },
            ["obv"] = new[] { "obv", "on-balance volume", "on balance volume" },
            ["vwap"] = new[] { "vwap", "volume weighted average" },
            ["ichimoku"] = new[] { "ichimoku", "kumo", "tenkan", "kijun" },
            ["parabolicsar"] = new[] { "parabolic sar", "psar" },
            ["roc"] = new[] { "roc", "rate of change" },
            ["keltner"] = new[] { "keltner" },
            ["donchian"] = new[] { "donchian" },
            ["pivotpoints"] = new[] { "pivot point", "pivot" },
            ["zscore"] = new[] { "zscore", "z-score", "z score" }
        };

        foreach (var kvp in indicatorKeywords)
        {
            if (kvp.Value.Any(keyword => textLower.Contains(keyword)))
            {
                indicators.Add(kvp.Key);
            }
        }

        // Always include price for crossover strategies
        if (!indicators.Contains("price"))
        {
            indicators.Add("price");
        }

        _context?.Logger.LogInformation($"Detected indicators from text: [{string.Join(", ", indicators)}]");
        return indicators;
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

        var systemInstruction = @"You are an expert quantitative trading strategy analyst and JSON schema generator.
YOUR ROLE: Extract trading strategy information from conversations and generate PERFECT, COMPLETE JSON templates.

CRITICAL REQUIREMENTS:
1. Return ONLY pure JSON - absolutely NO markdown, NO code blocks, NO explanations
2. Every field must be filled with meaningful, realistic values
3. All strings must have actual content - NO empty strings
4. All numeric values must be realistic trading parameters
5. Use null ONLY for optional indicator/value fields based on condition type
6. Ensure all JSON syntax is perfect (proper quotes, commas, brackets)
7. NEVER include stop_loss or take_profit conditions - these are configured separately during strategy creation
8. Use STEPWISE format with longEntrySteps, shortEntrySteps, longExitSteps, shortExitSteps";

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

JSON SCHEMA (STEPWISE FORMAT):
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
      ""parameters"": {{
        ""period"": {{
          ""type"": ""number"",
          ""label"": ""Label"",
          ""min"": 1,
          ""max"": 200,
          ""defaultValue"": 14,
          ""required"": true,
          ""description"": ""Description""
        }}
      }}
    }}
  ],
  ""LongEntrySteps"": [
    {{
      ""stepOrder"": 1,
      ""stepName"": ""T1: Entry Condition"",
      ""description"": ""Description of what triggers this step"",
      ""conditions"": [
        {{
          ""id"": ""a1b2c3d4-e5f6-7890-abcd-ef1234567890"",
          ""type"": ""below|above|crossover|crossunder"",
          ""description"": ""Condition description"",
          ""indicator"": ""uuid-of-indicator-for-above-below-or-null"",
          ""value"": 30,
          ""indicator1"": ""uuid-of-first-indicator-for-crossover-or-null"",
          ""indicator2"": ""uuid-of-second-indicator-for-crossover-or-null"",
          ""parameters"": {{
            ""value"": {{
              ""type"": ""number"",
              ""label"": ""Threshold Level"",
              ""min"": 0,
              ""max"": 100,
              ""defaultValue"": 30,
              ""step"": 1,
              ""required"": true,
              ""description"": ""Threshold description""
            }}
          }}
        }}
      ],
      ""isMandatory"": true
    }},
    {{
      ""stepOrder"": 2,
      ""stepName"": ""T2: Confirmation"",
      ""description"": ""Optional confirmation step"",
      ""conditions"": [...],
      ""isMandatory"": false
    }}
  ],
  ""LongExitSteps"": [
    {{
      ""stepOrder"": 1,
      ""stepName"": ""T1: Exit Signal"",
      ""description"": ""Exit trigger"",
      ""conditions"": [...],
      ""isMandatory"": true
    }}
  ],
  ""ShortEntrySteps"": [...],
  ""ShortExitSteps"": [...]
}}

IMPORTANT RULES:
1. Use PascalCase ONLY for step array names: LongEntrySteps, LongExitSteps, ShortEntrySteps, ShortExitSteps
2. Use camelCase for all other properties (name, description, indicators, id, type, stepOrder, conditions, isMandatory, etc.)
3. Each step array can have 1-3 steps (T1, T2, T3)
4. T1 is always isMandatory: true
5. T2, T3 are usually isMandatory: false (optional confirmations)
6. Steps execute in order - T2 only checks after T1 is satisfied
7. For above/below conditions, add a ""parameters"" object with a ""value"" parameter definition (the key must be ""value"" to match the condition's value field)
8. If conversation mentions ONLY profit targets and stop losses (no indicator exits), use empty conditions array for exit steps
9. ALL four step arrays MUST be present (LongEntrySteps, LongExitSteps, ShortEntrySteps, ShortExitSteps)
10. If direction is LONG only, still include ShortEntrySteps and ShortExitSteps with empty conditions
11. If direction is SHORT only, still include LongEntrySteps and LongExitSteps with empty conditions

Return ONLY the JSON, no explanations.";

        try
        {
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[] { new { text = promptMessage } }
                    }
                },
                systemInstruction = new
                {
                    parts = new[] { new { text = systemInstruction } }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    topK = 40,
                    topP = 0.95,
                    maxOutputTokens = 8192
                }
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            
            _context?.Logger.LogInformation($"Phase 3: Calling Gemini API ({_model}) for template generation...");
            var startTime = DateTime.UtcNow;
            
            var response = await _httpClient.PostAsync(url, content);
            
            var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
            _context?.Logger.LogInformation($"Phase 3: Gemini API responded in {elapsed:F1} seconds");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _context?.Logger.LogError($"Gemini API error: {errorContent}");
                throw new HttpRequestException($"Gemini API error: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var geminiResponse = JsonConvert.DeserializeObject<GeminiResponse>(responseContent);

            if (geminiResponse?.Candidates == null || geminiResponse.Candidates.Length == 0)
            {
                throw new InvalidOperationException("No response generated from Gemini");
            }

            var responseText = geminiResponse.Candidates[0].Content.Parts[0].Text ?? "";
            _context?.Logger.LogInformation($"Phase 3: Received response with {responseText.Length} characters");

            // Extract JSON from response
            responseText = CleanJsonResponse(responseText);

            // Validate JSON
            try
            {
                JsonConvert.DeserializeObject<object>(responseText);
                _context?.Logger.LogInformation($"Generated template (first 500 chars): {responseText.Substring(0, Math.Min(500, responseText.Length))}");
                return responseText;
            }
            catch
            {
                throw new InvalidOperationException("Generated response is not valid JSON");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not HttpRequestException)
        {
            throw new HttpRequestException($"Gemini API error: {ex.Message}", ex);
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
            ["ichimoku"] = new IndicatorDefinition
            {
                IndicatorType = "ichimoku",
                DisplayName = "Ichimoku Cloud",
                ExampleId = "ichimoku-indicator-uuid",
                PromptSnippet = @"- ICHIMOKU CLOUD:
    type=""ICHIMOKU"", Generate a UUID for id (e.g., ""i0d1e2f3-a4b5-6789-3456-aaaaaaaaaaaa"")
    Components: Use separate indicators with types TENKAN_SEN, KIJUN_SEN, SENKOU_SPAN_A, SENKOU_SPAN_B, CHIKOU_SPAN, each with their own UUID
    parameters={""tenkanPeriod"": {""type"": ""number"", ""min"": 5, ""max"": 30, ""defaultValue"": 9}, ""kijunPeriod"": {""type"": ""number"", ""min"": 10, ""max"": 60, ""defaultValue"": 26}, ""senkouPeriod"": {""type"": ""number"", ""min"": 20, ""max"": 120, ""defaultValue"": 52}}"
            },
            ["keltner"] = new IndicatorDefinition
            {
                IndicatorType = "keltner",
                DisplayName = "Keltner Channel",
                ExampleId = "keltner-indicator-uuid",
                PromptSnippet = @"- KELTNER CHANNEL:
    type=""KELTNER"", Generate a UUID for id (e.g., ""k1e2f3a4-b5c6-7890-4567-bbbbbbbbbbbb"")
    For upper/middle/lower bands, use type=""KELTNER_UPPER"", ""KELTNER_MIDDLE"", ""KELTNER_LOWER"" with their own UUIDs
    parameters={""period"": {""type"": ""number"", ""min"": 5, ""max"": 100, ""defaultValue"": 20}, ""multiplier"": {""type"": ""number"", ""min"": 0.5, ""max"": 5, ""defaultValue"": 2}}"
            },
            ["donchian"] = new IndicatorDefinition
            {
                IndicatorType = "donchian",
                DisplayName = "Donchian Channel",
                ExampleId = "donchian-indicator-uuid",
                PromptSnippet = @"- DONCHIAN CHANNEL:
    type=""DONCHIAN"", Generate a UUID for id (e.g., ""d2f3a4b5-c6d7-8901-5678-cccccccccccc"")
    For upper/middle/lower bands, use type=""DONCHIAN_UPPER"", ""DONCHIAN_MIDDLE"", ""DONCHIAN_LOWER"" with their own UUIDs
    parameters={""period"": {""type"": ""number"", ""min"": 5, ""max"": 100, ""defaultValue"": 20}}"
            },
            ["pivotpoints"] = new IndicatorDefinition
            {
                IndicatorType = "pivotpoints",
                DisplayName = "Pivot Points",
                ExampleId = "pivot-indicator-uuid",
                PromptSnippet = @"- PIVOT POINTS:
    type=""PIVOTPOINTS"", Generate a UUID for id (e.g., ""p3a4b5c6-d7e8-9012-6789-dddddddddddd"")
    Levels: Use separate indicators with types PIVOT, R1, R2, R3, S1, S2, S3, each with their own UUID
    parameters={""pivotType"": {""type"": ""string"", ""label"": ""Pivot Type"", ""options"": [""standard"", ""fibonacci"", ""woodie"", ""camarilla""], ""defaultValue"": ""standard""}}"
            },
            ["zscore"] = new IndicatorDefinition
            {
                IndicatorType = "zscore",
                DisplayName = "Z-Score",
                ExampleId = "zscore-indicator-uuid",
                PromptSnippet = @"- Z-SCORE:
    type=""ZSCORE"", Generate a UUID for id (e.g., ""z4b5c6d7-e8f9-0123-7890-eeeeeeeeeeee"")
    parameters={""period"": {""type"": ""number"", ""min"": 5, ""max"": 200, ""defaultValue"": 20}}"
            },
            ["roc"] = new IndicatorDefinition
            {
                IndicatorType = "roc",
                DisplayName = "Rate of Change",
                ExampleId = "roc-indicator-uuid",
                PromptSnippet = @"- ROC (Rate of Change):
    type=""ROC"", Generate a UUID for id (e.g., ""r5c6d7e8-f9a0-1234-8901-ffffffffffff"")
    parameters={""period"": {""type"": ""number"", ""min"": 1, ""max"": 100, ""defaultValue"": 14}}"
            },
            ["vwap"] = new IndicatorDefinition
            {
                IndicatorType = "vwap",
                DisplayName = "VWAP",
                ExampleId = "vwap-indicator-uuid",
                PromptSnippet = @"- VWAP (Volume Weighted Average Price):
    type=""VWAP"", Generate a UUID for id (e.g., ""v6d7e8f9-a0b1-2345-9012-111111111111"")
    parameters={} (no parameters needed - resets each session)"
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

public class GeminiResponse
{
    [JsonProperty("candidates")]
    public GeminiCandidate[]? Candidates { get; set; }
}

public class GeminiCandidate
{
    [JsonProperty("content")]
    public GeminiContent Content { get; set; } = new();
}

public class GeminiContent
{
    [JsonProperty("parts")]
    public GeminiPart[] Parts { get; set; } = Array.Empty<GeminiPart>();
}

public class GeminiPart
{
    [JsonProperty("text")]
    public string Text { get; set; } = string.Empty;
}

