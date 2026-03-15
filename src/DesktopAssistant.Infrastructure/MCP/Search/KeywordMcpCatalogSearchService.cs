using System.Reflection;
using System.Text.Json;
using DesktopAssistant.Infrastructure.MCP.Models;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.Infrastructure.MCP.Search;

/// <summary>
/// Searches MCP servers by keywords.
/// Looks for matches in name, description, and tags with weighted ranking.
/// </summary>
public class KeywordMcpCatalogSearchService : IMcpCatalogSearchService
{
    private readonly ILogger<KeywordMcpCatalogSearchService> _logger;
    private McpServersCatalog? _catalog;

    public KeywordMcpCatalogSearchService(ILogger<KeywordMcpCatalogSearchService> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<McpCatalogEntry>> SearchAsync(string query, int maxResults = 5)
    {
        var catalog = await LoadCatalogAsync();

        if (catalog.Servers.Count == 0)
            return [];

        var queryWords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return catalog.Servers
            .Select(s => new { Server = s, Score = CalculateRelevanceScore(s, queryWords) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => x.Server)
            .ToList();
    }

    private async Task<McpServersCatalog> LoadCatalogAsync()
    {
        if (_catalog != null)
            return _catalog;

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "DesktopAssistant.Infrastructure.MCP.Resources.mcp-servers-catalog.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
                _catalog = await JsonSerializer.DeserializeAsync<McpServersCatalog>(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading MCP servers catalog");
        }

        _catalog ??= new McpServersCatalog();
        return _catalog;
    }

    private static int CalculateRelevanceScore(McpCatalogEntry server, string[] queryWords)
    {
        int score = 0;
        var descLower = server.Description.ToLowerInvariant();
        var nameLower = server.Name.ToLowerInvariant();
        var tagsLower = server.Tags.Select(t => t.ToLowerInvariant()).ToList();

        foreach (var word in queryWords)
        {
            if (nameLower.Contains(word)) score += 10;
            if (descLower.Contains(word)) score += 5;
            if (tagsLower.Any(t => t.Contains(word))) score += 8;
            if (tagsLower.Contains(word)) score += 3; // Exact tag match
        }

        return score;
    }
}
