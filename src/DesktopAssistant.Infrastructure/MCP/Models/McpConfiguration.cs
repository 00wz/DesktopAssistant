using System.Text.Json.Serialization;

namespace DesktopAssistant.Infrastructure.MCP.Models;

/// <summary>
/// Root MCP configuration (contents of mcp.json).
/// </summary>
public class McpConfiguration
{
    /// <summary>
    /// Dictionary of MCP servers: key is the server name, value is its configuration.
    /// </summary>
    [JsonPropertyName("mcpServers")]
    public Dictionary<string, McpServerConfig> McpServers { get; set; } = new();
}
