using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using AlgoXera.Lambda.TemplateEngine.Models;
using Newtonsoft.Json;

namespace AlgoXera.Lambda.TemplateEngine.Repositories;

public class DynamoDbIndicatorRepository : IIndicatorRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private const string TableName = "IndicatorDefinitions";

    // In-memory cache for indicator definitions (they rarely change)
    private static List<IndicatorDefinition>? _cachedIndicators;
    private static DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public DynamoDbIndicatorRepository(IAmazonDynamoDB dynamoDb)
    {
        _dynamoDb = dynamoDb;
    }

    public async Task<List<IndicatorDefinition>> GetAllActiveAsync()
    {
        // Check cache first
        if (_cachedIndicators != null && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedIndicators.Where(i => i.IsActive).ToList();
        }

        var request = new ScanRequest
        {
            TableName = TableName,
            FilterExpression = "IsActive = :isActive",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":isActive"] = new AttributeValue { BOOL = true }
            }
        };

        var response = await _dynamoDb.ScanAsync(request);
        var indicators = response.Items.Select(MapToIndicatorDefinition).ToList();

        // Update cache
        _cachedIndicators = indicators;
        _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);

        return indicators;
    }

    public async Task<List<IndicatorDefinition>> GetByTypesAsync(IEnumerable<string> indicatorTypes)
    {
        var typeList = indicatorTypes.Select(t => t.ToLowerInvariant()).Distinct().ToList();
        
        if (!typeList.Any())
            return new List<IndicatorDefinition>();

        // Use BatchGetItem for efficiency
        var keys = typeList.Select(type => new Dictionary<string, AttributeValue>
        {
            ["IndicatorType"] = new AttributeValue { S = type }
        }).ToList();

        var request = new BatchGetItemRequest
        {
            RequestItems = new Dictionary<string, KeysAndAttributes>
            {
                [TableName] = new KeysAndAttributes { Keys = keys }
            }
        };

        var response = await _dynamoDb.BatchGetItemAsync(request);
        
        if (response.Responses.TryGetValue(TableName, out var items))
        {
            return items.Select(MapToIndicatorDefinition).ToList();
        }

        return new List<IndicatorDefinition>();
    }

    public async Task<IndicatorDefinition?> GetByTypeAsync(string indicatorType)
    {
        var request = new GetItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["IndicatorType"] = new AttributeValue { S = indicatorType.ToLowerInvariant() }
            }
        };

        var response = await _dynamoDb.GetItemAsync(request);
        
        return response.Item.Count == 0 ? null : MapToIndicatorDefinition(response.Item);
    }

    public async Task<Dictionary<string, string>> GetKeywordMappingsAsync()
    {
        var indicators = await GetAllActiveAsync();
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var indicator in indicators)
        {
            // Add the type itself
            mappings[indicator.IndicatorType] = indicator.IndicatorType;
            mappings[indicator.DisplayName.ToLowerInvariant()] = indicator.IndicatorType;

            // Add all aliases
            foreach (var alias in indicator.Aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    mappings[alias.ToLowerInvariant()] = indicator.IndicatorType;
                }
            }

            // Add all keywords
            foreach (var keyword in indicator.Keywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    mappings[keyword.ToLowerInvariant()] = indicator.IndicatorType;
                }
            }
        }

        return mappings;
    }

    public async Task<IndicatorDefinition> UpsertAsync(IndicatorDefinition definition)
    {
        definition.IndicatorType = definition.IndicatorType.ToLowerInvariant();
        definition.UpdatedAt = DateTime.UtcNow;
        
        if (definition.CreatedAt == default)
            definition.CreatedAt = DateTime.UtcNow;

        var request = new PutItemRequest
        {
            TableName = TableName,
            Item = MapToAttributeValues(definition)
        };

        await _dynamoDb.PutItemAsync(request);

        // Invalidate cache
        _cachedIndicators = null;

        return definition;
    }

    public async Task DeleteAsync(string indicatorType)
    {
        var request = new DeleteItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["IndicatorType"] = new AttributeValue { S = indicatorType.ToLowerInvariant() }
            }
        };

        await _dynamoDb.DeleteItemAsync(request);

        // Invalidate cache
        _cachedIndicators = null;
    }

    private Dictionary<string, AttributeValue> MapToAttributeValues(IndicatorDefinition definition)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["IndicatorType"] = new AttributeValue { S = definition.IndicatorType },
            ["DisplayName"] = new AttributeValue { S = definition.DisplayName },
            ["Category"] = new AttributeValue { S = definition.Category },
            ["Description"] = new AttributeValue { S = definition.Description },
            ["IdFormat"] = new AttributeValue { S = definition.IdFormat },
            ["ExampleId"] = new AttributeValue { S = definition.ExampleId },
            ["ParametersJson"] = new AttributeValue { S = definition.ParametersJson },
            ["PromptSnippet"] = new AttributeValue { S = definition.PromptSnippet },
            ["IsActive"] = new AttributeValue { BOOL = definition.IsActive },
            ["SortOrder"] = new AttributeValue { N = definition.SortOrder.ToString() },
            ["CreatedAt"] = new AttributeValue { S = definition.CreatedAt.ToString("O") },
            ["UpdatedAt"] = new AttributeValue { S = definition.UpdatedAt.ToString("O") }
        };

        if (definition.Aliases.Any())
        {
            item["Aliases"] = new AttributeValue { SS = definition.Aliases };
        }

        if (definition.Keywords.Any())
        {
            item["Keywords"] = new AttributeValue { SS = definition.Keywords };
        }

        return item;
    }

    private IndicatorDefinition MapToIndicatorDefinition(Dictionary<string, AttributeValue> item)
    {
        return new IndicatorDefinition
        {
            IndicatorType = item["IndicatorType"].S,
            DisplayName = item.GetValueOrDefault("DisplayName")?.S ?? "",
            Category = item.GetValueOrDefault("Category")?.S ?? "",
            Description = item.GetValueOrDefault("Description")?.S ?? "",
            IdFormat = item.GetValueOrDefault("IdFormat")?.S ?? "",
            ExampleId = item.GetValueOrDefault("ExampleId")?.S ?? "",
            ParametersJson = item.GetValueOrDefault("ParametersJson")?.S ?? "{}",
            PromptSnippet = item.GetValueOrDefault("PromptSnippet")?.S ?? "",
            Aliases = item.GetValueOrDefault("Aliases")?.SS ?? new List<string>(),
            Keywords = item.GetValueOrDefault("Keywords")?.SS ?? new List<string>(),
            IsActive = item.GetValueOrDefault("IsActive")?.BOOL ?? true,
            SortOrder = int.TryParse(item.GetValueOrDefault("SortOrder")?.N, out var order) ? order : 0,
            CreatedAt = DateTime.TryParse(item.GetValueOrDefault("CreatedAt")?.S, out var created) ? created : DateTime.UtcNow,
            UpdatedAt = DateTime.TryParse(item.GetValueOrDefault("UpdatedAt")?.S, out var updated) ? updated : DateTime.UtcNow
        };
    }
}

