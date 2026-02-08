using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Newtonsoft.Json;

namespace AlgoXera.Lambda.TemplateEngine.Services;

public class BedrockService
{
    private readonly AmazonBedrockRuntimeClient _bedrockClient;
    private readonly string _promptArn;

    public BedrockService(string promptArn = "arn:aws:bedrock:ap-south-1:428021717924:prompt/WD76YPY3TY")
    {
        _bedrockClient = new AmazonBedrockRuntimeClient(Amazon.RegionEndpoint.APSouth1);
        _promptArn = promptArn;
    }

    public async Task<string> GenerateStepwiseTemplateAsync(
        string conversationSummary,
        string templateName,
        string templateDescription,
        string templateCategory)
    {
        // Build comprehensive prompt with detailed instructions matching Gemini version
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
3. All indicator IDs must use format: type_period (e.g., ""rsi_14"", ""ema_20"", ""sma_50"")
4. Indicator parameters must be DEFINITIONS with type/label/min/max/defaultValue
5. For price comparisons, add price indicator: {{""id"":""close"",""type"":""PRICE"",""parameters"":{{}}}}
6. For MACD crossovers, define TWO indicators: ""macd_12_26_9"" and ""macd_signal_12_26_9""

SUPPORTED INDICATORS:
- RSI: id=""rsi_14"", type=""RSI"", parameters={{period}}
- EMA: id=""ema_20"", type=""EMA"", parameters={{period}}
- SMA: id=""sma_50"", type=""SMA"", parameters={{period}}
- MACD: id=""macd_12_26_9"", type=""MACD"", parameters={{fastPeriod,slowPeriod,signalPeriod}}
- PRICE: id=""close"", type=""PRICE"", parameters={{}}
- BOLLINGER BANDS: id=""bb_20"", type=""BOLLINGERBANDS"", parameters={{period,standardDeviations}}
- ATR: id=""atr_14"", type=""ATR"", parameters={{period}}
- ADX: id=""adx_14"", type=""ADX"", parameters={{period}}
- STOCHASTIC: id=""stoch_14_3"", type=""STOCHASTIC"", parameters={{kPeriod,dPeriod,smoothing}}
- SUPERTREND: id=""supertrend_10_3"", type=""SUPERTREND"", parameters={{period,multiplier}}

CONDITION ID FORMAT:
- **CRITICAL**: All condition IDs MUST be 32-character UUIDs (e.g., ""a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6"")
- DO NOT use descriptive names like ""long_entry_condition_1"" or ""rsi_oversold""
- Generate random UUID strings for each condition
- Example valid IDs: ""f3e2d1c0b9a8f7e6d5c4b3a2f1e0d9c8"", ""a9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4""

CONDITION TYPES:
1. 'above' or 'below': Compare indicator to threshold
   - Set: indicator, value
   - Null: indicator1, indicator2
   Example: {{""id"":""a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6"",""type"":""above"",""indicator"":""rsi_14"",""value"":""70"",""indicator1"":null,""indicator2"":null}}

2. 'crossover' or 'crossunder': Compare two indicators
   - Set: indicator1, indicator2
   - Null: indicator, value
   Example: {{""id"":""f9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4"",""type"":""crossover"",""indicator"":null,""value"":null,""indicator1"":""close"",""indicator2"":""ema_20""}}

JSON SCHEMA:
{{
  ""name"": ""{templateName}"",
  ""description"": ""{templateDescription}"",
  ""version"": ""1.0"",
  ""category"": ""{templateCategory}"",
  ""indicators"": [
    {{
      ""id"": ""rsi_14"",
      ""type"": ""RSI"",
      ""label"": ""RSI 14"",
      ""parameters"": {{
        ""period"": {{
          ""type"": ""number"",
          ""label"": ""Period"",
          ""min"": 2,
          ""max"": 50,
          ""defaultValue"": 14,
          ""required"": true,
          ""description"": ""RSI lookback period""
        }}
      }}
    }}
  ],
  ""longEntrySteps"": [
    {{
      ""stepOrder"": 1,
      ""stepName"": ""T1: Entry Condition"",
      ""description"": ""Description of what triggers entry"",
      ""conditions"": [
        {{
          ""id"": ""a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6"",
          ""type"": ""below"",
          ""description"": ""Condition description"",
          ""indicator"": ""rsi_14"",
          ""value"": ""30"",
          ""indicator1"": null,
          ""indicator2"": null
        }}
      ],
      ""isMandatory"": true
    }}
  ],
  ""longExitSteps"": [...],
  ""shortEntrySteps"": [...],
  ""shortExitSteps"": [...]
}}

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
                
                // Extract JSON from the response (handle markdown code blocks)
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
                
                responseText = responseText.Trim();
                
                // Validate it's proper JSON
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
}

