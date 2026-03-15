namespace DesktopAssistant.Infrastructure.MCP.Models;

/// <summary>
/// Result of an MCP tool invocation.
/// </summary>
public class McpToolResult
{
    /// <summary>
    /// Whether the invocation succeeded.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Text content of the result.
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Error message (when IsSuccess == false).
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static McpToolResult Success(string content) => new()
    {
        IsSuccess = true,
        Content = content
    };

    /// <summary>
    /// Creates an error result.
    /// </summary>
    public static McpToolResult Error(string errorMessage) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage
    };
}
