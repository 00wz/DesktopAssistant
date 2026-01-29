using System.Text.Json.Serialization;

namespace DesktopAssistant.Infrastructure.MCP.Models;

/// <summary>
/// Корневая конфигурация MCP (содержимое mcp.json)
/// </summary>
public class McpConfiguration
{
    /// <summary>
    /// Словарь MCP серверов: ключ - имя сервера, значение - конфигурация
    /// </summary>
    [JsonPropertyName("mcpServers")]
    public Dictionary<string, McpServerConfig> McpServers { get; set; } = new();
}
