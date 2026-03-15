using System.Text.Json;

namespace DesktopAssistant.Infrastructure.MCP.Models;

/// <summary>
/// Information about an MCP server tool.
/// </summary>
public class McpToolInfo
{
    /// <summary>
    /// Tool name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Tool description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// JSON Schema of the tool's parameters.
    /// </summary>
    public JsonElement? InputSchema { get; set; }
}
