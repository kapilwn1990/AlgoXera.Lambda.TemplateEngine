using AlgoXera.Lambda.TemplateEngine.Models;

namespace AlgoXera.Lambda.TemplateEngine.Repositories;

public interface ITemplateRepository
{
    Task<Template?> GetByIdAsync(string templateId);
    Task<List<Template>> GetByUserIdAsync(string userId);
    Task<Template> CreateAsync(Template template);
    Task<Template> UpdateAsync(Template template);
    Task DeleteAsync(string templateId);
}

