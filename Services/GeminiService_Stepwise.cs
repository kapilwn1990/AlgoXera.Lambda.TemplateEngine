using Amazon.Lambda.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace AlgoXera.Lambda.TemplateEngine.Services;

/// <summary>
/// Extension methods for GeminiService to support stepwise template generation
/// </summary>
public static class GeminiServiceStepwiseExtensions
{
    /// <summary>
    /// Generates a stepwise template with sequential T1→T2→T3 logic
    /// This is a simpler approach - just add this method to the existing GeminiService class
    /// </summary>
    public static async Task<string> GenerateStepwiseTemplateAsync(
        this GeminiService service,
        string conversationSummary,
        string name,
        string description,
        string category,
        ILambdaContext context,
        string apiKey)
    {
        var systemInstruction = @"You are an expert quantitative trading strategy analyst specializing in SEQUENTIAL, STEPWISE trading strategies.

YOUR ROLE: Extract trading strategy information and generate STEPWISE JSON templates with T1→T2→T3 sequential logic.

CRITICAL REQUIREMENTS:
1. Return ONLY pure JSON - NO markdown, NO code blocks, NO explanations
2. Generate SEQUENTIAL STEPS (T1, T2, T3, etc.) NOT single-time conditions
3. Each step represents a condition that must be met IN ORDER
4. Steps trigger sequentially: T1 → T2 → T3 → Entry/Exit
5. All fields must have meaningful, realistic values
6. Perfect JSON syntax (quotes, commas, brackets)
7. NEVER include stop_loss or take_profit - configured separately
8. **CRITICAL**: When referencing indicators in conditions, use the EXACT indicator 'id' from the indicators array
9. **CRITICAL**: Indicator parameters must be DEFINITIONS with type/label/min/max/defaultValue, NOT raw values
10. **CRITICAL**: If you need to compare CURRENT price with indicators, you MUST add a price indicator with id ""close"" and type ""PRICE"" to the indicators array
11. **CRITICAL - PREVIOUS CANDLE DATA**: If the strategy mentions comparing with PREVIOUS candle (previous close, previous high, previous low), you MUST:
    - Add PREV_CLOSE indicator (id: ""prev_close"", type: ""PREV_CLOSE"") for previous close comparisons
    - Add PREV_HIGH indicator (id: ""prev_high"", type: ""PREV_HIGH"") for previous high comparisons
    - Add PREV_LOW indicator (id: ""prev_low"", type: ""PREV_LOW"") for previous low comparisons
    - NEVER compare PRICE indicator with itself
    - Example: ""Current close higher than previous close"" requires BOTH ""close"" (PRICE) AND ""prev_close"" (PREV_CLOSE) indicators
12. **CRITICAL**: If the user requests an UNSUPPORTED indicator (not in the SUPPORTED INDICATORS list), you MUST return an error response in this format:
    {{
      ""error"": true,
      ""message"": ""The indicator '{indicator_name}' is not currently supported. Supported indicators are: RSI, EMA, SMA, MACD, BOLLINGER BANDS, ATR, ADX, STOCHASTIC, SUPERTREND, CCI, WILLIAMS %R, MFI, OBV, PARABOLIC SAR, PRICE, PREV_HIGH, PREV_LOW, and PREV_CLOSE. Would you like to use one of these alternatives instead?"",
      ""unsupportedIndicators"": [""indicator_name""],
      ""suggestedAlternatives"": [""similar_supported_indicator""]
    }}
12. **CRITICAL**: If the user's strategy can be built with supported indicators, suggest the closest match instead of returning an error.";

        var prompt = $@"
CONVERSATION TO ANALYZE:
{conversationSummary}

STEPWISE TEMPLATE TO CREATE:
Name: ""{name}""
Description: ""{description}""
Category: ""{category}""

======================================
SUPPORTED INDICATORS & NAMING CONVENTIONS
======================================

**CRITICAL**: Use ONLY these indicators with EXACT naming conventions.

**IF USER REQUESTS UNSUPPORTED INDICATOR:**
- Return an error response (see requirement #11 above)
- Suggest the closest supported alternative
- Example: If user asks for Ichimoku Cloud, suggest using EMA and SuperTrend as alternatives
- Example: If user asks for VWAP, suggest using SMA or EMA with OBV for volume-based signals

**SUPPORTED INDICATORS LIST:**

1. **PRICE (Close Price)**
   - ID Format: ""close""
   - Type: ""PRICE""
   - When to use: Comparing CURRENT price to indicators (e.g., current price above EMA)
   - **DO NOT USE FOR PREVIOUS CANDLE** - Use PREV_CLOSE instead
   - Parameters: {{}} (empty - no parameters)
   - Example:
     {{
       ""id"": ""close"",
       ""type"": ""PRICE"",
       ""label"": ""Close Price"",
       ""parameters"": {{}}
     }}

2. **PREV_HIGH (Previous High)**
   - ID Format: ""prev_high""
   - Type: ""PREV_HIGH""
   - When to use: Compare current price with previous candle's high
   - **CRITICAL**: Use this when strategy mentions ""previous high"", ""yesterday's high"", ""last candle's high""
   - Parameters: {{}} (empty - no parameters)
   - Example: Price breaks above previous high
   - Example usage:
     {{
       ""id"": ""prev_high"",
       ""type"": ""PREV_HIGH"",
       ""label"": ""Previous High"",
       ""parameters"": {{}}
     }}

3. **PREV_LOW (Previous Low)**
   - ID Format: ""prev_low""
   - Type: ""PREV_LOW""
   - When to use: Compare current price with previous candle's low
   - **CRITICAL**: Use this when strategy mentions ""previous low"", ""yesterday's low"", ""last candle's low""
   - Parameters: {{}} (empty - no parameters)
   - Example: Price breaks below previous low
   - Example usage:
     {{
       ""id"": ""prev_low"",
       ""type"": ""PREV_LOW"",
       ""label"": ""Previous Low"",
       ""parameters"": {{}}
     }}

4. **PREV_CLOSE (Previous Close)**
   - ID Format: ""prev_close""
   - Type: ""PREV_CLOSE""
   - When to use: Compare current price with previous candle's close
   - **CRITICAL**: Use this when strategy mentions ""previous close"", ""yesterday's close"", ""last candle's close""
   - **NEVER use PRICE indicator twice** - If comparing current vs previous close, use ""close"" (PRICE) + ""prev_close"" (PREV_CLOSE)
   - Parameters: {{}} (empty - no parameters)
   - Example: ""Current close higher than previous close"" → crossover with indicator1=""close"", indicator2=""prev_close""
   - Example usage:
     {{
       ""id"": ""prev_close"",
       ""type"": ""PREV_CLOSE"",
       ""label"": ""Previous Close"",
       ""parameters"": {{}}
     }}

6. **RSI (Relative Strength Index)**
   - ID Format: ""rsi_{{period}}"" (e.g., ""rsi_14"", ""rsi_21"")
   - Type: ""RSI""
   - Common values: RSI(14) - standard momentum oscillator
   - Ranges: 0-100 (oversold < 30, overbought > 70)
   - Parameter: period (2-50, default: 14)

7. **EMA (Exponential Moving Average)**
   - ID Format: ""ema_{{period}}"" (e.g., ""ema_20"", ""ema_50"", ""ema_200"")
   - Type: ""EMA""
   - Common values: EMA(20) short-term, EMA(50) medium-term, EMA(200) long-term
   - Used for: Trend direction, dynamic support/resistance
   - Parameter: period (2-200, default: 20)

8. **SMA (Simple Moving Average)**
   - ID Format: ""sma_{{period}}"" (e.g., ""sma_20"", ""sma_50"", ""sma_200"")
   - Type: ""SMA""
   - Common values: SMA(20) short-term, SMA(50) medium-term, SMA(200) long-term
   - Used for: Trend identification, slower than EMA
   - Parameter: period (2-200, default: 20)

9. **MACD (Moving Average Convergence Divergence)**
   - **CRITICAL**: MACD requires TWO separate indicators for crossover strategies
   - ID Format for MACD line: ""macd_{{fast}}_{{slow}}_{{signal}}"" (e.g., ""macd_12_26_9"")
   - ID Format for Signal line: ""macd_signal_{{fast}}_{{slow}}_{{signal}}"" (e.g., ""macd_signal_12_26_9"")
   - Type: ""MACD"" (for both indicators)
   - Standard: MACD(12,26,9)
   - Used for: Trend momentum, crossovers between MACD line and Signal line
   - Parameters: fastPeriod (2-50, default: 12), slowPeriod (2-100, default: 26), signalPeriod (2-50, default: 9)
   
   **HOW TO USE MACD CROSSOVERS:**
   - Define TWO indicators with identical parameters:
     1. MACD line: {{""id"": ""macd_12_26_9"", ""type"": ""MACD"", ""parameters"": {{""fastPeriod"": {{""defaultValue"": 12}}, ""slowPeriod"": {{""defaultValue"": 26}}, ""signalPeriod"": {{""defaultValue"": 9}}}}}}
     2. Signal line: {{""id"": ""macd_signal_12_26_9"", ""type"": ""MACD"", ""parameters"": {{""fastPeriod"": {{""defaultValue"": 12}}, ""slowPeriod"": {{""defaultValue"": 26}}, ""signalPeriod"": {{""defaultValue"": 9}}}}}}
   - Use generic 'crossover' or 'crossunder' condition type
   - Set indicator1=""macd_12_26_9"", indicator2=""macd_signal_12_26_9""
   - Example bullish entry: {{""type"": ""crossover"", ""indicator1"": ""macd_12_26_9"", ""indicator2"": ""macd_signal_12_26_9"", ""indicator"": null, ""value"": null}}
   - Example bearish entry: {{""type"": ""crossunder"", ""indicator1"": ""macd_12_26_9"", ""indicator2"": ""macd_signal_12_26_9"", ""indicator"": null, ""value"": null}}
   
   **CRITICAL RULES:**
   - NEVER use indicator1=""macd_12_26_9"" and indicator2=""macd_12_26_9"" (self-comparison!)
   - ALWAYS use different IDs: one with ""macd_"" prefix, one with ""macd_signal_"" prefix
   - Both indicators MUST have identical parameter values
   - Use generic 'crossover'/'crossunder' condition types (NOT 'macd_crossover')

10. **BOLLINGER BANDS**
   - ID Format: ""bb_{{period}}"" (e.g., ""bb_20"")
   - Type: ""BOLLINGERBANDS""
   - Standard: BB(20,2) - 20 period with 2 standard deviations
   - Used for: Volatility, overbought/oversold
   - Parameters: period (2-100, default: 20), standardDeviations (1-4, default: 2)

11. **ATR (Average True Range)**
   - ID Format: ""atr_{{period}}"" (e.g., ""atr_14"")
   - Type: ""ATR""
   - Standard: ATR(14)
   - Used for: Volatility measurement, stop-loss calculation
   - Parameter: period (2-50, default: 14)

12. **ADX (Average Directional Index)**
   - ID Format: ""adx_{{period}}"" (e.g., ""adx_14"")
   - Type: ""ADX""
   - Standard: ADX(14)
   - Ranges: 0-100 (strong trend > 25, weak trend < 20)
   - Used for: Trend strength measurement
   - Parameter: period (2-50, default: 14)

13. **STOCHASTIC**
   - ID Format: ""stoch_{{k}}_{{d}}"" (e.g., ""stoch_14_3"")
   - Type: ""STOCHASTIC""
   - Standard: Stochastic(14,3,3)
   - Ranges: 0-100 (oversold < 20, overbought > 80)
   - Parameters: kPeriod (2-50, default: 14), dPeriod (2-50, default: 3), smoothing (1-10, default: 3)

14. **SUPERTREND**
    - ID Format: ""supertrend_{{period}}_{{multiplier}}"" (e.g., ""supertrend_10_3"")
    - Type: ""SUPERTREND""
    - Standard: SuperTrend(10,3)
    - Used for: Trend following, dynamic support/resistance
    - Parameters: period (2-50, default: 10), multiplier (1-10, default: 3)

15. **CCI (Commodity Channel Index)**
    - ID Format: ""cci_{{period}}"" (e.g., ""cci_20"")
    - Type: ""CCI""
    - Standard: CCI(20)
    - Ranges: -100 to +100 (oversold < -100, overbought > +100)
    - Parameter: period (2-50, default: 20)

16. **WILLIAMS %R**
    - ID Format: ""williamsr_{{period}}"" (e.g., ""williamsr_14"")
    - Type: ""WILLIAMSR""
    - Standard: Williams %R(14)
    - Ranges: -100 to 0 (oversold < -80, overbought > -20)
    - Parameter: period (2-50, default: 14)

17. **MFI (Money Flow Index)**
    - ID Format: ""mfi_{{period}}"" (e.g., ""mfi_14"")
    - Type: ""MFI""
    - Standard: MFI(14)
    - Ranges: 0-100 (oversold < 20, overbought > 80)
    - Parameter: period (2-50, default: 14)

18. **OBV (On Balance Volume)**
    - ID Format: ""obv""
    - Type: ""OBV""
    - Used for: Volume-based momentum
    - Parameters: {{}} (no parameters)

19. **PARABOLIC SAR**
    - ID Format: ""psar""
    - Type: ""PSAR""
    - Used for: Trend following, stop-loss placement
    - Parameters: accelerationStep (0.01-0.1, default: 0.02), maxAcceleration (0.1-1.0, default: 0.2)

**NAMING CONVENTION RULES:**
1. Always use lowercase for indicator IDs
2. Include the primary parameter value in the ID (e.g., ""rsi_14"" not ""rsi_main"")
3. For multiple instances of same type, use different parameter values (e.g., ""ema_20"" and ""ema_50"")
4. Price indicator MUST be ""close"" (never ""price_close"" or other variants)
5. Multi-parameter indicators: use format ""indicator_param1_param2"" (e.g., ""macd_12_26_9"")

======================================
STEPWISE JSON SCHEMA
======================================

ROOT STRUCTURE:
{{
  ""name"": ""{name}"",
  ""description"": ""{description}"",
  ""version"": ""1.0"",
  ""category"": ""{category}"",
  ""indicators"": [ /* array of indicators */ ],
  ""longEntrySteps"": [ /* array of sequential steps */ ],
  ""longExitSteps"": [ /* array of sequential steps */ ],
  ""shortEntrySteps"": [ /* array of sequential steps */ ],
  ""shortExitSteps"": [ /* array of sequential steps */ ]
}}

INDICATOR DEFINITION:
{{
  ""id"": ""indicator_period"",         // CRITICAL: Use format ""type_period"" (e.g., ""rsi_14"", ""ema_20"", ""sma_50"")
  ""type"": ""INDICATOR_TYPE"",         // UPPERCASE: RSI, EMA, SMA, MACD, PRICE, SUPERTREND, ADX, etc.
  ""label"": ""Display Name"",
  ""parameters"": {{
    ""period"": {{
      ""type"": ""number"",
      ""label"": ""Period"",
      ""min"": 2,
      ""max"": 50,
      ""defaultValue"": 14,
      ""required"": true,
      ""description"": ""Parameter description""
    }}
  }}
}}

**EXAMPLES:**
- RSI with period 14: id=""rsi_14"", defaultValue=14
- EMA with period 20: id=""ema_20"", defaultValue=20  
- ADX with period 14: id=""adx_14"", defaultValue=14
- Multiple EMAs: id=""ema_20"" and id=""ema_50"" (different periods, different IDs)

**SPECIAL CASE - PRICE INDICATOR** (use when comparing price to other indicators):
{{
  ""id"": ""close"",                    // MUST be ""close"" (not ""price_close"" or other variants)
  ""type"": ""PRICE"",                  // Type is PRICE
  ""label"": ""Close Price"",
  ""parameters"": {{}}                  // Empty - no parameters needed for price
}}

STEP DEFINITION:
{{
  ""stepOrder"": 1,                     // Sequential: 1, 2, 3...
  ""stepName"": ""T1: Description"",    // T1, T2, T3... format
  ""description"": ""What this step checks"",
  ""conditions"": [
    {{
      ""id"": ""condition_id"",
      ""type"": ""below|above|crossover|crossunder"",
      ""description"": ""What this condition checks"",
      
      // **FOR BELOW/ABOVE CONDITIONS** (indicator vs threshold value):
      // Example: RSI below 30
      ""indicator"": ""rsi_14"",       // **REQUIRED**: MUST MATCH an indicator.id from indicators array
      ""value"": ""30"",                // **REQUIRED**: Threshold value as STRING
      ""indicator1"": null,             // **MUST BE NULL** for below/above
      ""indicator2"": null,             // **MUST BE NULL** for below/above
      
      // **FOR CROSSOVER/CROSSUNDER CONDITIONS** (indicator1 crosses indicator2):
      // Example: EMA fast crosses above EMA slow OR MACD crosses Signal
      ""indicator1"": ""ema_12"",      // **REQUIRED**: First indicator (the one crossing) - e.g., ""ema_12"" or ""macd_12_26_9""
      ""indicator2"": ""ema_26"",      // **REQUIRED**: Second indicator (being crossed) - e.g., ""ema_26"" or ""macd_signal_12_26_9""
      ""indicator"": null,              // **MUST BE NULL** for crossover/crossunder
      ""value"": null                   // **MUST BE NULL** for crossover/crossunder
    }}
  ],
  ""isMandatory"": true
}}

**CONDITION TYPE RULES:**
1. 'below' or 'above': Use when comparing ONE indicator to a FIXED NUMBER
   - Set: indicator, value
   - Set to null: indicator1, indicator2
   - Example: RSI above 70 = type:'above', indicator:'rsi_14', value:'70', indicator1:null, indicator2:null
   - ⚙️ **CONFIGURABLE**: Users can change the threshold value (e.g., 70 → 75)

2. 'crossover' or 'crossunder': Use when comparing TWO indicators
   - Set: indicator1, indicator2
   - Set to null: indicator, value
   - Examples:
     - EMA crossover: type:'crossover', indicator1:'ema_12', indicator2:'ema_50', indicator:null, value:null
     - MACD crossover: type:'crossover', indicator1:'macd_12_26_9', indicator2:'macd_signal_12_26_9', indicator:null, value:null
   - ❌ **NOT CONFIGURABLE**: Crossovers have no threshold - just comparing two indicators
   - **CRITICAL FOR MACD**: Both indicators MUST be defined in the indicators array with identical parameters
       ""indicator2"": null
     }}
   - **DO NOT use generic 'crossover'/'crossunder' with MACD indicators** - this will fail!
   - **DO NOT define separate macd_main and macd_signal indicators** - system handles automatically
   - Users configure MACD parameters (fast/slow/signal periods) in the MACD indicator definition

**IMPORTANT - CONFIGURABLE vs NON-CONFIGURABLE CONDITIONS:**
- CONFIGURABLE: 'below', 'above', 'equals', 'greater_than', 'less_than' (have threshold value parameter)
- NOT CONFIGURABLE: 'crossover', 'crossunder' (no threshold - just indicator comparison)
- Users CANNOT change crossover conditions - they can only change indicator parameters (periods)
- Users CAN change threshold values for below/above conditions (e.g., RSI < 30 → RSI < 25)

COMPLETE EXAMPLE - RSI Mean Reversion Strategy:
{{
  ""name"": ""RSI Mean Reversion"",
  ""description"": ""Buy oversold, sell overbought using RSI"",
  ""version"": ""1.0"",
  ""category"": ""Mean Reversion"",
  ""indicators"": [
    {{
      ""id"": ""rsi_14"",
      ""type"": ""RSI"",
      ""label"": ""RSI 14"",
      ""parameters"": {{
        ""period"": {{
          ""type"": ""number"",
          ""label"": ""RSI Period"",
          ""min"": 2,
          ""max"": 50,
          ""defaultValue"": 14,
          ""required"": true,
          ""description"": ""Lookback period for RSI calculation""
        }}
      }}
    }}
  ],
  ""longEntrySteps"": [
    {{
      ""stepOrder"": 1,
      ""stepName"": ""T1: RSI Drops Below 30"",
      ""description"": ""Wait for RSI to enter oversold territory"",
      ""conditions"": [
        {{
          ""id"": ""long_rsi_oversold"",
          ""type"": ""below"",
          ""description"": ""RSI below 30 indicates oversold"",
          ""indicator"": ""rsi_14"",
          ""value"": ""30"",
          ""indicator1"": null,
          ""indicator2"": null
        }}
      ],
      ""isMandatory"": true
    }}
  ],
  ""longExitSteps"": [
    {{
      ""stepOrder"": 1,
      ""stepName"": ""T1: RSI Rises Above 70"",
      ""description"": ""Exit when RSI reaches overbought"",
      ""conditions"": [
        {{
          ""id"": ""exit_long_rsi_overbought"",
          ""type"": ""above"",
          ""description"": ""RSI above 70 indicates overbought, take profit"",
          ""indicator"": ""rsi_14"",
          ""value"": ""70"",
          ""indicator1"": null,
          ""indicator2"": null
        }}
      ],
      ""isMandatory"": true
    }}
  ],
  ""shortEntrySteps"": [
    {{
      ""stepOrder"": 1,
      ""stepName"": ""T1: RSI Rises Above 70"",
      ""description"": ""Wait for RSI to enter overbought territory"",
      ""conditions"": [
        {{
          ""id"": ""short_rsi_overbought"",
          ""type"": ""above"",
          ""description"": ""RSI above 70 indicates overbought"",
          ""indicator"": ""rsi_14"",
          ""value"": ""70"",
          ""indicator1"": null,
          ""indicator2"": null
        }}
      ],
      ""isMandatory"": true
    }}
  ],
  ""shortExitSteps"": [
    {{
      ""stepOrder"": 1,
      ""stepName"": ""T1: RSI Drops Below 30"",
      ""description"": ""Exit when RSI reaches oversold"",
      ""conditions"": [
        {{
          ""id"": ""exit_short_rsi_oversold"",
          ""type"": ""below"",
          ""description"": ""RSI below 30 indicates oversold, take profit"",
          ""indicator"": ""rsi_14"",
          ""value"": ""30"",
          ""indicator1"": null,
          ""indicator2"": null
        }}
      ],
      ""isMandatory"": true
    }}
  ]
}}

**CRITICAL VALIDATION BEFORE RETURNING:**
1. Check ALL conditions in ALL steps
2. For each condition, verify the ""indicator"" field (or ""indicator1""/""indicator2"") references an indicator.id that EXISTS in the indicators array
3. Indicator IDs must match EXACTLY (case-insensitive is OK: ""rsi_14"" or ""RSI_14"")
4. Common mistake: condition says ""indicator"": ""RSI"" but indicators array has ""id"": ""rsi_14"" → WRONG! Use ""rsi_14""
5. **CRITICAL**: If any condition uses ""close"" as indicator/indicator1/indicator2, you MUST have a price indicator in the indicators array:
   {{
     ""id"": ""close"",
     ""type"": ""PRICE"",
     ""label"": ""Close Price"",
     ""parameters"": {{}}
   }}
6. **CRITICAL CONDITION FIELD RULES**:
   - For type 'above' or 'below': indicator must NOT be null, value must NOT be null, indicator1 MUST be null, indicator2 MUST be null
   - For type 'crossover' or 'crossunder': indicator MUST be null, value MUST be null, indicator1 must NOT be null, indicator2 must NOT be null

CORRECT CONDITION EXAMPLES:
// RSI above 70 (comparing indicator to threshold)
{{
  ""type"": ""above"",
  ""indicator"": ""rsi_14"",
  ""value"": ""70"",
  ""indicator1"": null,
  ""indicator2"": null
}}

// Price crosses above EMA (comparing two indicators)
{{
  ""type"": ""crossover"",
  ""indicator"": null,
  ""value"": null,
  ""indicator1"": ""close"",
  ""indicator2"": ""ema_20""
}}

// ❌ WRONG: Current close higher than previous close (comparing PRICE with itself)
{{
  ""type"": ""crossover"",
  ""indicator"": null,
  ""value"": null,
  ""indicator1"": ""close"",
  ""indicator2"": ""close""  // ❌ ERROR: Cannot compare indicator with itself!
}}

// ✅ CORRECT: Current close higher than previous close (PRICE vs PREV_CLOSE)
{{
  ""type"": ""crossover"",
  ""indicator"": null,
  ""value"": null,
  ""indicator1"": ""close"",       // Current close (PRICE indicator)
  ""indicator2"": ""prev_close""   // Previous close (PREV_CLOSE indicator)
}}
// And you MUST include BOTH indicators in the indicators array:
// 1. {{ ""id"": ""close"", ""type"": ""PRICE"", ""label"": ""Close Price"", ""parameters"": {{}} }}
// 2. {{ ""id"": ""prev_close"", ""type"": ""PREV_CLOSE"", ""label"": ""Previous Close"", ""parameters"": {{}} }}

WRONG EXAMPLES TO AVOID:
// WRONG - above condition with null indicator
{{
  ""type"": ""above"",
  ""indicator"": null,  // ❌ WRONG!
  ""value"": ""70"",
  ""indicator1"": ""rsi_14"",
  ""indicator2"": null
}}

// WRONG - crossover with indicator instead of indicator1/indicator2
{{
  ""type"": ""crossover"",
  ""indicator"": ""close"",  // ❌ WRONG!
  ""value"": null,
  ""indicator1"": null,
  ""indicator2"": ""ema_20""
}}

// WRONG - MACD self-comparison
{{
  ""type"": ""crossover"",  // ❌ WRONG! indicator1 and indicator2 are the same!
  ""description"": ""MACD crosses Signal"",
  ""indicator"": null,
  ""value"": null,
  ""indicator1"": ""macd_12_26_9"",  // ❌ WRONG! Both indicators are the same
  ""indicator2"": ""macd_12_26_9""   // ❌ WRONG! This is self-comparison - no crossover possible!
}}

// CORRECT MACD - Use two separate indicators
{{
  ""type"": ""crossover"",  // ✅ CORRECT
  ""description"": ""MACD crosses above Signal line"",
  ""indicator"": null,
  ""value"": null,
  ""indicator1"": ""macd_12_26_9"",  // ✅ CORRECT - MACD line
  ""indicator2"": ""macd_signal_12_26_9""  // ✅ CORRECT - Signal line (different ID!)
}}

EXAMPLE - Price Crossover Strategy (shows proper price indicator usage):
{{
  ""name"": ""EMA Crossover"",
  ""description"": ""Buy when price crosses above EMA, sell when crosses below"",
  ""version"": ""1.0"",
  ""category"": ""Trend Following"",
  ""indicators"": [
    {{
      ""id"": ""close"",
      ""type"": ""PRICE"",
      ""label"": ""Close Price"",
      ""parameters"": {{}}
    }},
    {{
      ""id"": ""ema_20"",
      ""type"": ""EMA"",
      ""label"": ""EMA 20"",
      ""parameters"": {{
        ""period"": {{
          ""type"": ""number"",
          ""label"": ""EMA Period"",
          ""min"": 2,
          ""max"": 200,
          ""defaultValue"": 20,
          ""required"": true,
          ""description"": ""EMA lookback period""
        }}
      }}
    }}
  ],
  ""longEntrySteps"": [
    {{
      ""stepOrder"": 1,
      ""stepName"": ""T1: Price Crosses Above EMA"",
      ""description"": ""Wait for bullish crossover"",
      ""conditions"": [
        {{
          ""id"": ""long_price_cross_ema"",
          ""type"": ""crossover"",
          ""description"": ""Price crosses above EMA indicating uptrend"",
          ""indicator"": null,
          ""value"": null,
          ""indicator1"": ""close"",
          ""indicator2"": ""ema_20""
        }}
      ],
      ""isMandatory"": true
    }}
  ],
  ""longExitSteps"": [
    {{
      ""stepOrder"": 1,
      ""stepName"": ""T1: Price Crosses Below EMA"",
      ""description"": ""Exit on bearish crossover"",
      ""conditions"": [
        {{
          ""id"": ""exit_long_price_cross_ema"",
          ""type"": ""crossunder"",
          ""description"": ""Price crosses below EMA, exit long"",
          ""indicator"": null,
          ""value"": null,
          ""indicator1"": ""close"",
          ""indicator2"": ""ema_20""
        }}
      ],
      ""isMandatory"": true
    }}
  ],
  ""shortEntrySteps"": [],
  ""shortExitSteps"": []
}}

EXAMPLE - MACD Crossover Strategy (shows proper two-indicator MACD approach):
{{
  ""name"": ""MACD Crossover"",
  ""description"": ""Buy when MACD crosses above Signal, sell when crosses below"",
  ""version"": ""1.0"",
  ""category"": ""Trend Following"",
  ""indicators"": [
    {{
      ""id"": ""macd_12_26_9"",
      ""type"": ""MACD"",
      ""label"": ""MACD Line (12,26,9)"",
      ""parameters"": {{
        ""fastPeriod"": {{
          ""type"": ""number"",
          ""label"": ""Fast Period"",
          ""min"": 2,
          ""max"": 50,
          ""defaultValue"": 12,
          ""required"": true,
          ""description"": ""Fast EMA period""
        }},
        ""slowPeriod"": {{
          ""type"": ""number"",
          ""label"": ""Slow Period"",
          ""min"": 2,
          ""max"": 100,
          ""defaultValue"": 26,
          ""required"": true,
          ""description"": ""Slow EMA period""
        }},
        ""signalPeriod"": {{
          ""type"": ""number"",
          ""label"": ""Signal Period"",
          ""min"": 2,
          ""max"": 50,
          ""defaultValue"": 9,
          ""required"": true,
          ""description"": ""Signal line period""
        }}
      }}
    }},
    {{
      ""id"": ""macd_signal_12_26_9"",
      ""type"": ""MACD"",
      ""label"": ""MACD Signal Line (12,26,9)"",
      ""parameters"": {{
        ""fastPeriod"": {{
          ""type"": ""number"",
          ""label"": ""Fast Period"",
          ""min"": 2,
          ""max"": 50,
          ""defaultValue"": 12,
          ""required"": true,
          ""description"": ""Fast EMA period""
        }},
        ""slowPeriod"": {{
          ""type"": ""number"",
          ""label"": ""Slow Period"",
          ""min"": 2,
          ""max"": 100,
          ""defaultValue"": 26,
          ""required"": true,
          ""description"": ""Slow EMA period""
        }},
        ""signalPeriod"": {{
          ""type"": ""number"",
          ""label"": ""Signal Period"",
          ""min"": 2,
          ""max"": 50,
          ""defaultValue"": 9,
          ""required"": true,
          ""description"": ""Signal line period""
        }}
      }}
    }}
  ],
  ""longEntrySteps"": [
    {{
      ""stepOrder"": 1,
      ""stepName"": ""T1: MACD Bullish Crossover"",
      ""description"": ""MACD line crosses above Signal line"",
      ""conditions"": [
        {{
          ""id"": ""long_macd_cross"",
          ""type"": ""crossover"",
          ""description"": ""MACD crosses above Signal (bullish)"",
          ""indicator"": null,
          ""value"": null,
          ""indicator1"": ""macd_12_26_9"",
          ""indicator2"": ""macd_signal_12_26_9""
        }}
      ],
      ""isMandatory"": true
    }}
  ],
  ""longExitSteps"": [
    {{
      ""stepOrder"": 1,
      ""stepName"": ""T1: MACD Bearish Crossunder"",
      ""description"": ""MACD line crosses below Signal line"",
      ""conditions"": [
        {{
          ""id"": ""exit_long_macd_cross"",
          ""type"": ""crossunder"",
          ""description"": ""MACD crosses below Signal (bearish)"",
          ""indicator"": null,
          ""value"": null,
          ""indicator1"": ""macd_12_26_9"",
          ""indicator2"": ""macd_signal_12_26_9""
        }}
      ],
      ""isMandatory"": true
    }}
  ],
  ""shortEntrySteps"": [],
  ""shortExitSteps"": []
}}

EXAMPLE - MACD Crossover Strategy (APPROACH 2 - Advanced with separate indicators):
{{
  ""name"": ""MACD Crossover Advanced"",
  ""description"": ""Buy when MACD crosses above Signal using separate indicators"",
  ""version"": ""1.0"",
  ""category"": ""Trend Following"",
  ""indicators"": [
    {{
      ""id"": ""macd_12_26_9"",
      ""type"": ""MACD"",
      ""label"": ""MACD Line (12,26,9)"",
      ""parameters"": {{
        ""fastPeriod"": {{
          ""type"": ""number"",
          ""label"": ""Fast Period"",
          ""min"": 2,
          ""max"": 50,
          ""defaultValue"": 12,
          ""required"": true,
          ""description"": ""Fast EMA period""
        }},
        ""slowPeriod"": {{
          ""type"": ""number"",
          ""label"": ""Slow Period"",
          ""min"": 2,
          ""max"": 100,
          ""defaultValue"": 26,
          ""required"": true,
          ""description"": ""Slow EMA period""
        }},
        ""signalPeriod"": {{
          ""type"": ""number"",
          ""label"": ""Signal Period"",
          ""min"": 2,
          ""max"": 50,
          ""defaultValue"": 9,
          ""required"": true,
          ""description"": ""Signal line period""
        }}
      }}
    }},
    {{
      ""id"": ""macd_signal_12_26_9"",
      ""type"": ""MACD"",
      ""label"": ""MACD Signal (12,26,9)"",
      ""parameters"": {{
        ""fastPeriod"": {{
          ""type"": ""number"",
          ""label"": ""Fast Period"",
          ""min"": 2,
          ""max"": 50,
          ""defaultValue"": 12,
          ""required"": true,
          ""description"": ""Fast EMA period""
        }},
        ""slowPeriod"": {{
          ""type"": ""number"",
          ""label"": ""Slow Period"",
          ""min"": 2,
          ""max"": 100,
          ""defaultValue"": 26,
          ""required"": true,
          ""description"": ""Slow EMA period""
        }},
        ""signalPeriod"": {{
          ""type"": ""number"",
          ""label"": ""Signal Period"",
          ""min"": 2,
          ""max"": 50,
          ""defaultValue"": 9,
          ""required"": true,
          ""description"": ""Signal line period""
        }}
      }}
    }}
  ],
  ""longEntrySteps"": [
    {{
      ""stepOrder"": 1,
      ""stepName"": ""T1: MACD Crosses Above Signal"",
      ""description"": ""MACD line crosses above Signal line"",
      ""conditions"": [
        {{
          ""id"": ""long_macd_cross_signal"",
          ""type"": ""crossover"",
          ""description"": ""MACD crosses above Signal (bullish)"",
          ""indicator"": null,
          ""value"": null,
          ""indicator1"": ""macd_12_26_9"",
          ""indicator2"": ""macd_signal_12_26_9""
        }}
      ],
      ""isMandatory"": true
    }}
  ],
  ""longExitSteps"": [
    {{
      ""stepOrder"": 1,
      ""stepName"": ""T1: MACD Crosses Below Signal"",
      ""description"": ""MACD line crosses below Signal line"",
      ""conditions"": [
        {{
          ""id"": ""exit_long_macd_cross_signal"",
          ""type"": ""crossunder"",
          ""description"": ""MACD crosses below Signal (bearish)"",
          ""indicator"": null,
          ""value"": null,
          ""indicator1"": ""macd_12_26_9"",
          ""indicator2"": ""macd_signal_12_26_9""
        }}
      ],
      ""isMandatory"": true
    }}
  ],
  ""shortEntrySteps"": [],
  ""shortExitSteps"": []
}}

**CRITICAL MACD REMINDERS:**
- ✅ CORRECT: Define TWO separate indicators with different IDs
  - MACD line: id=""macd_12_26_9"", type=""MACD""
  - Signal line: id=""macd_signal_12_26_9"", type=""MACD""
- ✅ CORRECT: Both indicators must have IDENTICAL parameter values (fastPeriod, slowPeriod, signalPeriod)
- ✅ CORRECT: Use generic 'crossover'/'crossunder' condition types
- ✅ CORRECT: Set indicator1=""macd_12_26_9"", indicator2=""macd_signal_12_26_9""
- ✅ CORRECT: Set indicator=null, value=null for crossover conditions

**WRONG PATTERNS TO AVOID:**
- ❌ WRONG: indicator1=""macd_12_26_9"" and indicator2=""macd_12_26_9"" (self-comparison - will generate 0 trades!)
- ❌ WRONG: Using condition types 'macd_crossover' or 'macd_crossunder' (not supported)
- ❌ WRONG: Defining only ONE MACD indicator (need both line and signal)
- ❌ WRONG: Different parameters between macd and macd_signal indicators (must be identical!)
- ❌ WRONG: Using IDs like ""macd_main"" or ""macd_signal"" without parameter suffixes


Generate the complete stepwise template JSON now:";


        using var httpClient = new HttpClient();
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = prompt } }
                }
            },
            systemInstruction = new
            {
                parts = new[] { new { text = systemInstruction } }
            },
            generationConfig = new
            {
                temperature = 0.6,  // Reduced from 0.7 for more focused, faster responses
                topK = 40,
                topP = 0.95,
                responseMimeType = "application/json"  // Force JSON response format
                // No maxOutputTokens limit - let Gemini use as many tokens as needed
            }
        };

        var json = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
        var response = await httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            context.Logger.LogError($"Gemini API error: {errorContent}");
            throw new Exception($"Gemini API failed: {response.StatusCode}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        context.Logger.LogInformation($"Raw Gemini response (first 500 chars): {responseContent.Substring(0, Math.Min(500, responseContent.Length))}");
        
        var geminiResponse = JsonConvert.DeserializeObject<GeminiResponseData>(responseContent);

        if (geminiResponse?.Candidates == null || geminiResponse.Candidates.Length == 0)
        {
            context.Logger.LogError($"No candidates in Gemini response. Full response: {responseContent}");
            throw new Exception("No response from Gemini API");
        }

        if (geminiResponse.Candidates[0]?.Content?.Parts == null || geminiResponse.Candidates[0].Content.Parts.Length == 0)
        {
            context.Logger.LogError($"No parts in candidate. Full response: {responseContent}");
            throw new Exception("Invalid Gemini API response structure");
        }

        var generatedText = geminiResponse.Candidates[0].Content.Parts[0].Text;
        
        if (string.IsNullOrEmpty(generatedText))
        {
            context.Logger.LogError($"Empty text in Gemini response. Full response: {responseContent}");
            throw new Exception("Empty response from Gemini API");
        }
        
        // Clean up markdown code blocks if present
        generatedText = generatedText.Replace("```json", "").Replace("```", "").Trim();
        
        context.Logger.LogInformation($"Generated stepwise template JSON (first 500 chars): {generatedText.Substring(0, Math.Min(500, generatedText.Length))}");
        
        // Auto-correct common AI mistakes with condition field structure
        generatedText = AutoCorrectConditionFields(generatedText, context);
        
        return generatedText;
    }
    
    private static string AutoCorrectConditionFields(string jsonText, ILambdaContext context)
    {
        try
        {
            var template = JsonConvert.DeserializeObject<JObject>(jsonText);
            if (template == null) return jsonText;
            
            bool corrected = false;
            var sections = new[] { "longEntrySteps", "longExitSteps", "shortEntrySteps", "shortExitSteps" };
            
            foreach (var section in sections)
            {
                var steps = template[section] as JArray;
                if (steps == null) continue;
                
                foreach (var step in steps)
                {
                    var conditions = step["conditions"] as JArray;
                    if (conditions == null) continue;
                    
                    foreach (var condition in conditions)
                    {
                        var type = condition["type"]?.ToString()?.ToLower();
                        if (type == null) continue;
                        
                        // Debug: Log the entire condition JSON
                        context.Logger.LogInformation($"[AUTO-CORRECT DEBUG] Raw condition JSON: {condition.ToString(Formatting.None)}");
                        
                        // For above/below: indicator and value should be set, indicator1/indicator2 should be null
                        if (type == "above" || type == "below")
                        {
                            var indicator = condition["indicator"];
                            var indicator1 = condition["indicator1"];
                            var indicator2 = condition["indicator2"];
                            var value = condition["value"];
                            
                            // Debug: Log what AI generated
                            context.Logger.LogInformation($"[AUTO-CORRECT DEBUG] {type} condition - indicator: '{indicator}', indicator1: '{indicator1}', indicator2: '{indicator2}', value: '{value}'");
                            
                            // Common mistake #1: AI sets indicator1 instead of indicator
                            if ((indicator == null || indicator.Type == JTokenType.Null || string.IsNullOrEmpty(indicator.ToString())) &&
                                indicator1 != null && indicator1.Type != JTokenType.Null && !string.IsNullOrEmpty(indicator1.ToString()))
                            {
                                context.Logger.LogInformation($"[AUTO-CORRECT] Moving indicator1 '{indicator1}' to indicator for {type} condition");
                                condition["indicator"] = indicator1;
                                condition["indicator1"] = null;
                                corrected = true;
                            }
                            // Common mistake #2: AI sets indicator2 instead of indicator
                            else if ((indicator == null || indicator.Type == JTokenType.Null || string.IsNullOrEmpty(indicator.ToString())) &&
                                indicator2 != null && indicator2.Type != JTokenType.Null && !string.IsNullOrEmpty(indicator2.ToString()))
                            {
                                context.Logger.LogInformation($"[AUTO-CORRECT] Moving indicator2 '{indicator2}' to indicator for {type} condition");
                                condition["indicator"] = indicator2;
                                condition["indicator2"] = null;
                                corrected = true;
                            }
                        }
                        // For crossover/crossunder: indicator1 and indicator2 should be set, indicator and value should be null
                        else if (type == "crossover" || type == "crossunder")
                        {
                            var indicator = condition["indicator"];
                            var indicator1 = condition["indicator1"];
                            
                            // Common mistake: AI sets indicator instead of indicator1
                            if ((indicator1 == null || indicator1.Type == JTokenType.Null || string.IsNullOrEmpty(indicator1.ToString())) &&
                                indicator != null && indicator.Type != JTokenType.Null && !string.IsNullOrEmpty(indicator.ToString()))
                            {
                                context.Logger.LogInformation($"[AUTO-CORRECT] Moving indicator '{indicator}' to indicator1 for {type} condition");
                                condition["indicator1"] = indicator;
                                condition["indicator"] = null;
                                corrected = true;
                            }
                        }
                    }
                }
            }
            
            if (corrected)
            {
                var correctedJson = template.ToString(Formatting.None);
                context.Logger.LogInformation($"[AUTO-CORRECT] Applied field corrections to template");
                return correctedJson;
            }
            
            return jsonText;
        }
        catch (Exception ex)
        {
            context.Logger.LogWarning($"[AUTO-CORRECT] Failed to auto-correct: {ex.Message}. Returning original JSON.");
            return jsonText;
        }
    }
}

// Helper classes for Gemini API response
public class GeminiResponseData
{
    [JsonProperty("candidates")]
    public GeminiCandidateData[]? Candidates { get; set; }
}

public class GeminiCandidateData
{
    [JsonProperty("content")]
    public GeminiContentData Content { get; set; } = new();
}

public class GeminiContentData
{
    [JsonProperty("parts")]
    public GeminiPartData[] Parts { get; set; } = Array.Empty<GeminiPartData>();
}

public class GeminiPartData
{
    [JsonProperty("text")]
    public string Text { get; set; } = string.Empty;
}

