using System.Text.Json.Serialization;

namespace DesktopAssistant.Infrastructure.MCP.Models;

/// <summary>
/// Конфигурация одного MCP сервера для mcp.json
/// </summary>
public class McpServerConfig
{
    /// <summary>
    /// Команда для запуска сервера (node, npx, python и т.д.)
    /// </summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;
    
    /// <summary>
    /// Аргументы командной строки
    /// </summary>
    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = new();
    
    /// <summary>
    /// Тип транспорта: stdio или http
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "stdio";
    
    /// <summary>
    /// Включён ли сервер
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Переменные окружения для процесса сервера
    /// </summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }
    
    /// <summary>
    /// URL для HTTP транспорта
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }
    
    /// <summary>
    /// Заголовки для HTTP транспорта
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }
}
