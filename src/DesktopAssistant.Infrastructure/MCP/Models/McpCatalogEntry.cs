using System.Text.Json.Serialization;

namespace DesktopAssistant.Infrastructure.MCP.Models;

/// <summary>
/// Запись в каталоге MCP серверов
/// </summary>
public class McpCatalogEntry
{
    /// <summary>
    /// Уникальный идентификатор сервера
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// Отображаемое имя
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Описание возможностей сервера
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// URL репозитория на GitHub
    /// </summary>
    [JsonPropertyName("githubUrl")]
    public string GitHubUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// Теги для поиска
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Каталог MCP серверов
/// </summary>
public class McpServersCatalog
{
    [JsonPropertyName("servers")]
    public List<McpCatalogEntry> Servers { get; set; } = new();
}
