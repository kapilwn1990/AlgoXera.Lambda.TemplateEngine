namespace AlgoXera.Lambda.TemplateEngine.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? Username { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Template
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string RulesJson { get; set; } = string.Empty;
    public string Status { get; set; } = "draft";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Stepwise template support
    public bool IsStepwise { get; set; } = false;
    public string? LongEntryStepsJson { get; set; }
    public string? LongExitStepsJson { get; set; }
    public string? ShortEntryStepsJson { get; set; }
    public string? ShortExitStepsJson { get; set; }
    
    // Multi-Timeframe Support
    public string TemplateType { get; set; } = "Execution";
    public string? RulesJsonSignal { get; set; }
    public string? Direction { get; set; }
    public string? Timeframe { get; set; }
}

public class Conversation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public Guid? TemplateId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}

public class Message
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    
    public Conversation Conversation { get; set; } = null!;
}

public class TemplateRules
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public string Category { get; set; } = string.Empty;
    public List<Indicator> Indicators { get; set; } = new();
    public RuleGroup? LongEntry { get; set; }
    public RuleGroup? ShortEntry { get; set; }
    public RuleGroup ExitRules { get; set; } = new();
}

public class Indicator
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public Dictionary<string, ParameterDefinition> Parameters { get; set; } = new();
}

public class ParameterDefinition
{
    public string Type { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public object? DefaultValue { get; set; }
    public List<string>? Options { get; set; }
    public bool Required { get; set; } = true;
    public string? Description { get; set; }
}

public class RuleGroup
{
    public string Operator { get; set; } = "AND";
    public List<Condition> Conditions { get; set; } = new();
}

public class Condition
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Indicator1 { get; set; }
    public string? Indicator2 { get; set; }
    public string? Indicator { get; set; }
    public string? Operator { get; set; }
    public string? Value { get; set; }
    public string? Label { get; set; }
    public Dictionary<string, ParameterDefinition>? Parameters { get; set; }
    public string Description { get; set; } = string.Empty;
}

// ==========================================
// STEPWISE TEMPLATE MODELS
// ==========================================

/// <summary>
/// Represents a sequential step in a strategy rule
/// Steps are executed in order (T1 → T2 → T3)
/// </summary>
public class StrategyStep
{
    public int StepOrder { get; set; }  // 1, 2, 3 (T1, T2, T3)
    public string StepName { get; set; } = string.Empty;  // e.g., "T1: RSI Oversold", "T2: EMA Crossover"
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Conditions that must be met for this step to trigger
    /// All conditions are evaluated with AND logic within a step
    /// </summary>
    public List<StepCondition> Conditions { get; set; } = new();
    
    /// <summary>
    /// Whether this step must trigger for the strategy to proceed
    /// Optional steps can be skipped if not met
    /// </summary>
    public bool IsMandatory { get; set; } = true;
    
    /// <summary>
    /// AI-generated suggestion for this step (for wizard UI)
    /// </summary>
    public string? AISuggestion { get; set; }
}

/// <summary>
/// A condition within a strategy step
/// Similar to Condition but used in sequential step context
/// </summary>
public class StepCondition
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;  // crossover, crossunder, above, below
    public string Description { get; set; } = string.Empty;
    
    // For crossover/crossunder
    public string? Indicator1 { get; set; }
    public string? Indicator2 { get; set; }
    
    // For above/below
    public string? Indicator { get; set; }
    public string? Value { get; set; }
    
    // Optional configurable parameters (e.g., threshold values)
    public Dictionary<string, ParameterDefinition>? Parameters { get; set; }
    
    public string? Label { get; set; }
}

/// <summary>
/// Complete stepwise template rules structure
/// </summary>
public class StepwiseTemplateRules
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public string Category { get; set; } = string.Empty;
    
    // Indicators remain the same
    public List<Indicator> Indicators { get; set; } = new();
    
    // Sequential steps for each rule type
    public List<StrategyStep> LongEntrySteps { get; set; } = new();
    public List<StrategyStep> LongExitSteps { get; set; } = new();
    public List<StrategyStep> ShortEntrySteps { get; set; } = new();
    public List<StrategyStep> ShortExitSteps { get; set; } = new();
}

/// <summary>
/// Signal Template Rules - for higher timeframe (1D/4H/1H) signal generation
/// Uses simultaneous conditions (no stepwise T1→T2→T3 logic)
/// Supports EITHER Bullish OR Bearish direction (not both)
/// </summary>
public class SignalTemplateRules
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public string Category { get; set; } = string.Empty;
    public string Direction { get; set; } = "Bullish"; // "Bullish" or "Bearish"
    public string Timeframe { get; set; } = "1d"; // 1D, 4H, 1H
    
    // Indicators used in the signal template
    public List<Indicator> Indicators { get; set; } = new();
    
    // Simultaneous signal conditions (all evaluated at once, no steps)
    public List<StepCondition> SignalConditions { get; set; } = new();
}

