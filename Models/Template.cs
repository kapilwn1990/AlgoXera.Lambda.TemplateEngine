namespace AlgoXera.Lambda.TemplateEngine.Models;

public class Template
{
    public string TemplateId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "Custom";
    public string Status { get; set; } = "Draft"; // Draft, Active, Archived, GENERATING, FAILED
    public bool IsStepwise { get; set; }
    public string? RulesJson { get; set; }
    public string? ConversationId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Multi-Timeframe Support
    public string TemplateType { get; set; } = "Execution"; // "Execution" or "Signal"
    public string? RulesJsonSignal { get; set; } // For Signal templates (simultaneous conditions)
    public string? Direction { get; set; } // "Bullish" or "Bearish" (for Signal templates only)
    public string? Timeframe { get; set; } // Template timeframe (1D, 4H, 1H for Signal; 5m, 15m for Execution)
}

