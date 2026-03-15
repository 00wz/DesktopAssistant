using System.Text.Json;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// MCP server manager — manages connections and tool invocations.
/// </summary>
public interface IMcpServerManager
{
    /// <summary>
    /// Initializes the manager and connects to servers from the configuration.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects to the specified server.
    /// </summary>
    Task ConnectServerAsync(string serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the specified server.
    /// </summary>
    Task DisconnectServerAsync(string serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restarts the specified server.
    /// </summary>
    Task RestartServerAsync(string serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns information about all connected servers.
    /// </summary>
    IReadOnlyList<McpServerInfoDto> GetConnectedServers();

    /// <summary>
    /// Returns information about a specific server (including error status).
    /// </summary>
    McpServerInfoDto? GetServerInfo(string serverId);

    /// <summary>
    /// Returns all tools from all connected servers.
    /// </summary>
    IReadOnlyList<McpToolInfoDto> GetAllTools();

    /// <summary>
    /// Invokes a tool on the specified server.
    /// </summary>
    Task<McpToolResultDto> InvokeToolAsync(string serverId, string toolName, JsonElement arguments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Server status change event.
    /// </summary>
    event EventHandler<McpServerChangedEventArgs>? ServerChanged;
}

/// <summary>
/// DTO for MCP server information.
/// </summary>
public class McpServerInfoDto
{
    public string Id { get; set; } = string.Empty;
    public McpServerStatusDto Status { get; set; } = McpServerStatusDto.Disconnected;
    public List<McpToolInfoDto> Tools { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// MCP server status.
/// </summary>
public enum McpServerStatusDto
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

/// <summary>
/// DTO for MCP server tool information.
/// </summary>
public class McpToolInfoDto
{
    public string ServerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public JsonElement? InputSchema { get; set; }
}

/// <summary>
/// DTO for the result of a tool invocation.
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
/// Arguments for the server change event.
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
