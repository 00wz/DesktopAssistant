using DesktopAssistant.Infrastructure.MCP.Models;

namespace DesktopAssistant.Infrastructure.MCP.Search;

/// <summary>
/// Service for searching MCP servers in the catalog.
/// Allows switching search strategy (keyword, vector, etc.).
/// </summary>
public interface IMcpCatalogSearchService
{
    /// <summary>
    /// Searches MCP servers by query and returns results sorted by relevance.
    /// </summary>
    /// <param name="query">Search query or keywords.</param>
    /// <param name="maxResults">Maximum number of results.</param>
    Task<IReadOnlyList<McpCatalogEntry>> SearchAsync(string query, int maxResults = 5);
}
