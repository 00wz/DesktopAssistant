namespace DesktopAssistant.Infrastructure.MCP.Models;

/// <summary>
/// Информация о подключённом MCP сервере
/// </summary>
public class McpServerInfo
{
    /// <summary>
    /// Уникальный идентификатор сервера (имя в конфигурации)
    /// </summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// Статус подключения
    /// </summary>
    public McpServerStatus Status { get; set; } = McpServerStatus.Disconnected;
    
    /// <summary>
    /// Доступные инструменты сервера
    /// </summary>
    public List<McpToolInfo> Tools { get; set; } = new();
    
    /// <summary>
    /// Сообщение об ошибке (если Status == Error)
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Статус MCP сервера
/// </summary>
public enum McpServerStatus
{
    /// <summary>
    /// Отключён
    /// </summary>
    Disconnected,
    
    /// <summary>
    /// Подключение в процессе
    /// </summary>
    Connecting,
    
    /// <summary>
    /// Подключён и работает
    /// </summary>
    Connected,
    
    /// <summary>
    /// Ошибка подключения
    /// </summary>
    Error
}
