namespace DesktopAssistant.Infrastructure.MCP.Models;

/// <summary>
/// Information about a connected MCP server.
/// </summary>
public class McpServerInfo
{
    /// <summary>
    /// Unique server identifier (name from configuration).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Connection status.
    /// </summary>
    public McpServerStatus Status { get; set; } = McpServerStatus.Disconnected;

    /// <summary>
    /// Tools available on this server.
    /// </summary>
    public List<McpToolInfo> Tools { get; set; } = new();

    /// <summary>
    /// Error message (when Status == Error).
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// MCP server status.
/// </summary>
public enum McpServerStatus
{
    /// <summary>
    /// Disconnected.
    /// </summary>
    Disconnected,

    /// <summary>
    /// Connection in progress.
    /// </summary>
    Connecting,

    /// <summary>
    /// Connected and operational.
    /// </summary>
    Connected,

    /// <summary>
    /// Connection error.
    /// </summary>
    Error
}
