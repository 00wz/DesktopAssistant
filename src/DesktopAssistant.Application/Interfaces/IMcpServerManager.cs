using System.Text.Json;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Менеджер MCP серверов - управляет подключениями и вызовом tools
/// </summary>
public interface IMcpServerManager
{
    /// <summary>
    /// Инициализирует менеджер и подключается к серверам из конфигурации
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Подключается к указанному серверу
    /// </summary>
    Task ConnectServerAsync(string serverId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Отключается от указанного сервера
    /// </summary>
    Task DisconnectServerAsync(string serverId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Перезапускает указанный сервер
    /// </summary>
    Task RestartServerAsync(string serverId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Возвращает информацию о всех подключённых серверах
    /// </summary>
    IReadOnlyList<McpServerInfoDto> GetConnectedServers();
    
    /// <summary>
    /// Возвращает все tools со всех подключённых серверов
    /// </summary>
    IReadOnlyList<McpToolInfoDto> GetAllTools();
    
    /// <summary>
    /// Вызывает tool на указанном сервере
    /// </summary>
    Task<McpToolResultDto> InvokeToolAsync(string serverId, string toolName, JsonElement arguments, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Событие изменения статуса сервера
    /// </summary>
    event EventHandler<McpServerChangedEventArgs>? ServerChanged;
}

/// <summary>
/// DTO информации о MCP сервере
/// </summary>
public class McpServerInfoDto
{
    public string Id { get; set; } = string.Empty;
    public McpServerStatusDto Status { get; set; } = McpServerStatusDto.Disconnected;
    public List<McpToolInfoDto> Tools { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Статус MCP сервера
/// </summary>
public enum McpServerStatusDto
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

/// <summary>
/// DTO информации о tool MCP сервера
/// </summary>
public class McpToolInfoDto
{
    public string ServerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public JsonElement? InputSchema { get; set; }
}

/// <summary>
/// DTO результата вызова tool
/// </summary>
public class McpToolResultDto
{
    public bool IsSuccess { get; set; }
    public string? Content { get; set; }
    public string? ErrorMessage { get; set; }
    
    public static McpToolResultDto Success(string content) => new()
    {
        IsSuccess = true,
        Content = content
    };
    
    public static McpToolResultDto Error(string errorMessage) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Аргументы события изменения сервера
/// </summary>
public class McpServerChangedEventArgs : EventArgs
{
    public string ServerId { get; }
    public McpServerStatusDto NewStatus { get; }
    public string? ErrorMessage { get; }
    
    public McpServerChangedEventArgs(string serverId, McpServerStatusDto newStatus, string? errorMessage = null)
    {
        ServerId = serverId;
        NewStatus = newStatus;
        ErrorMessage = errorMessage;
    }
}
