using AlgoXera.Lambda.TemplateEngine.Models;

namespace AlgoXera.Lambda.TemplateEngine.Services;

/// <summary>
/// Interface for AI template generation services (Bedrock, Gemini, OpenAI, etc.)
/// </summary>
public interface IAITemplateService
{
    /// <summary>
    /// Generate a stepwise template from conversation summary
    /// </summary>
    Task<string> GenerateStepwiseTemplateAsync(
        string conversationSummary,
        string templateName,
        string templateDescription,
        string templateCategory);
}

