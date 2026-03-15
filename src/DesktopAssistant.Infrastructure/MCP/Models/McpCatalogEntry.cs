using System.Text.Json.Serialization;

namespace DesktopAssistant.Infrastructure.MCP.Models;

/// <summary>
/// Entry in the MCP server catalog.
/// </summary>
public class McpCatalogEntry
{
    /// <summary>
    /// Unique server identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of the server's capabilities.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// GitHub repository URL.
    /// </summary>
    [JsonPropertyName("githubUrl")]
    public string GitHubUrl { get; set; } = string.Empty;

    /// <summary>
    /// Tags used for searching.
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Catalog of MCP servers.
/// </summary>
public class McpServersCatalog
{
    [JsonPropertyName("servers")]
    public List<McpCatalogEntry> Servers { get; set; } = new();
}
