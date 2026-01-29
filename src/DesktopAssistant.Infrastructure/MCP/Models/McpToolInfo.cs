using System.Text.Json;

namespace DesktopAssistant.Infrastructure.MCP.Models;

/// <summary>
/// Информация об инструменте MCP сервера
/// </summary>
public class McpToolInfo
{
    /// <summary>
    /// Имя инструмента
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Описание инструмента
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// JSON Schema параметров инструмента
    /// </summary>
    public JsonElement? InputSchema { get; set; }
}
