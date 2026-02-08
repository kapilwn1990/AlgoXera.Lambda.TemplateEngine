using AlgoXera.Lambda.TemplateEngine.DTOs;
using AlgoXera.Lambda.TemplateEngine.Models;

namespace AlgoXera.Lambda.TemplateEngine.Services;

public interface ITemplateService
{
    Task<List<TemplateDto>> GetTemplatesAsync(string userId);
    Task<TemplateDto?> GetTemplateAsync(string templateId, string userId);
    Task<TemplateDto> CreateTemplateAsync(CreateTemplateRequest request, string userId);
    Task<TemplateDto> UpdateTemplateAsync(string templateId, CreateTemplateRequest request, string userId);
    Task DeleteTemplateAsync(string templateId, string userId);
    Task<TemplateDto> GenerateTemplateAsync(GenerateTemplateRequest request, string userId);
}

