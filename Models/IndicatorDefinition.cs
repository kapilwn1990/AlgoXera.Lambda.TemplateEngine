namespace AlgoXera.Lambda.TemplateEngine.Models;

/// <summary>
/// DynamoDB model for storing indicator definitions
/// Each indicator has its full prompt specification stored separately
/// </summary>
public class IndicatorDefinition
{
    /// <summary>
    /// Unique indicator identifier (Partition Key)
    /// Format: lowercase type (e.g., "rsi", "ema", "macd", "bollingerbands")
    /// </summary>
    public string IndicatorType { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the indicator
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Category for grouping (e.g., "Momentum", "Trend", "Volatility", "Volume")
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Brief description of what the indicator measures
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// ID format pattern (e.g., "rsi_{period}", "ema_{period}", "macd_{fast}_{slow}_{signal}")
    /// </summary>
    public string IdFormat { get; set; } = string.Empty;

    /// <summary>
    /// Example ID (e.g., "rsi_14", "ema_20", "macd_12_26_9")
    /// </summary>
    public string ExampleId { get; set; } = string.Empty;

    /// <summary>
    /// JSON string containing parameter definitions
    /// Example: {"period": {"type": "number", "label": "Period", "min": 2, "max": 50, "defaultValue": 14}}
    /// </summary>
    public string ParametersJson { get; set; } = string.Empty;

    /// <summary>
    /// Full prompt snippet for this indicator (used in AI prompt construction)
    /// </summary>
    public string PromptSnippet { get; set; } = string.Empty;

    /// <summary>
    /// Common aliases that might be mentioned in conversation
    /// (e.g., ["relative strength index", "rsi indicator", "momentum oscillator"] for RSI)
    /// </summary>
    public List<string> Aliases { get; set; } = new();

    /// <summary>
    /// Keywords for matching in conversation extraction
    /// </summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>
    /// Whether this indicator is active and available
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Sort order for display purposes
    /// </summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Lightweight model for indicator extraction phase
/// </summary>
public class ExtractedIndicator
{
    public string Type { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Response from indicator extraction phase
/// </summary>
public class IndicatorExtractionResult
{
    public List<ExtractedIndicator> Indicators { get; set; } = new();
    public string StrategyType { get; set; } = string.Empty; // "long-only", "short-only", "long-short"
    public string Summary { get; set; } = string.Empty;
}

