using System.Text.Json.Serialization;
using AlgoXera.Lambda.TemplateEngine.Entities;

namespace AlgoXera.Lambda.TemplateEngine.DTOs;

public class TemplateDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;
    
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    
    [JsonPropertyName("rulesJson")]
    public string? RulesJson { get; set; }
    
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
    
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
    
    // Stepwise template support
    [JsonPropertyName("isStepwise")]
    public bool IsStepwise { get; set; }
    
    [JsonPropertyName("longEntryStepsJson")]
    public string? LongEntryStepsJson { get; set; }
    
    [JsonPropertyName("longExitStepsJson")]
    public string? LongExitStepsJson { get; set; }
    
    [JsonPropertyName("shortEntryStepsJson")]
    public string? ShortEntryStepsJson { get; set; }
    
    [JsonPropertyName("shortExitStepsJson")]
    public string? ShortExitStepsJson { get; set; }
    
    // Error tracking for async generation
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
    
    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; set; }
    
    // Multi-Timeframe Support
    [JsonPropertyName("templateType")]
    public string TemplateType { get; set; } = "Execution";
    
    [JsonPropertyName("rulesJsonSignal")]
    public string? RulesJsonSignal { get; set; }
    
    [JsonPropertyName("direction")]
    public string? Direction { get; set; }
    
    [JsonPropertyName("timeframe")]
    public string? Timeframe { get; set; }
}

public class CreateTemplateRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;
    
    [JsonPropertyName("rules")]
    public TemplateRules Rules { get; set; } = null!;
}

public class GenerateTemplateRequest
{
    [JsonPropertyName("conversationId")]
    public Guid ConversationId { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;
    
    // Stepwise mode flag
    [JsonPropertyName("isStepwise")]
    public bool IsStepwise { get; set; } = false;
    
    // Multi-Timeframe Support
    [JsonPropertyName("templateType")]
    public string TemplateType { get; set; } = "Execution";
    
    [JsonPropertyName("direction")]
    public string? Direction { get; set; }
    
    [JsonPropertyName("timeframe")]
    public string? Timeframe { get; set; }
}

