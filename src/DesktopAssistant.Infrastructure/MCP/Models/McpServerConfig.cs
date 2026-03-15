using System.Text.Json.Serialization;

namespace DesktopAssistant.Infrastructure.MCP.Models;

/// <summary>
/// Configuration of a single MCP server for mcp.json.
/// </summary>
public class McpServerConfig
{
    /// <summary>
    /// Command to launch the server (node, npx, python, etc.).
    /// </summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Command-line arguments.
    /// </summary>
    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = new();

    /// <summary>
    /// Transport type: stdio or http.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "stdio";

    /// <summary>
    /// Whether the server is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Environment variables for the server process.
    /// </summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>
    /// URL for HTTP transport.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// Headers for HTTP transport.
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }
}
