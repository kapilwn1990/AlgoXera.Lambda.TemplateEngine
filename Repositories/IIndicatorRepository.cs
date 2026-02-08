using AlgoXera.Lambda.TemplateEngine.Models;

namespace AlgoXera.Lambda.TemplateEngine.Repositories;

public interface IIndicatorRepository
{
    /// <summary>
    /// Get all active indicator definitions
    /// </summary>
    Task<List<IndicatorDefinition>> GetAllActiveAsync();

    /// <summary>
    /// Get indicator definitions by their types
    /// </summary>
    Task<List<IndicatorDefinition>> GetByTypesAsync(IEnumerable<string> indicatorTypes);

    /// <summary>
    /// Get a single indicator definition by type
    /// </summary>
    Task<IndicatorDefinition?> GetByTypeAsync(string indicatorType);

    /// <summary>
    /// Get all keywords for indicator matching (for extraction phase)
    /// Returns a dictionary of keyword -> indicator type
    /// </summary>
    Task<Dictionary<string, string>> GetKeywordMappingsAsync();

    /// <summary>
    /// Create or update an indicator definition
    /// </summary>
    Task<IndicatorDefinition> UpsertAsync(IndicatorDefinition definition);

    /// <summary>
    /// Delete an indicator definition
    /// </summary>
    Task DeleteAsync(string indicatorType);
}

