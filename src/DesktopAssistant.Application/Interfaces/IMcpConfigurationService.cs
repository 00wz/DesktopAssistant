namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Service for managing MCP server configuration.
/// </summary>
public interface IMcpConfigurationService
{
    /// <summary>
    /// Path to the mcp.json configuration file.
    /// </summary>
    string ConfigFilePath { get; }

    /// <summary>
    /// Loads the configuration from the file.
    /// </summary>
    Task<McpConfigurationDto> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the configuration to the file.
    /// </summary>
    Task SaveAsync(McpConfigurationDto config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new server to the configuration.
    /// </summary>
    Task AddServerAsync(string serverId, McpServerConfigDto serverConfig, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a server from the configuration.
    /// </summary>
    Task RemoveServerAsync(string serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a server's configuration.
    /// </summary>
    Task UpdateServerAsync(string serverId, McpServerConfigDto serverConfig, CancellationToken cancellationToken = default);

    /// <summary>
    /// Configuration change event (raised by the FileWatcher).
    /// </summary>
    event EventHandler<McpConfigChangedEventArgs>? ConfigurationChanged;
}

/// <summary>
/// MCP configuration DTO.
/// </summary>
public class McpConfigurationDto
{
    public Dictionary<string, McpServerConfigDto> McpServers { get; set; } = new();
}

/// <summary>
/// DTO for a single MCP server configuration.
/// </summary>
public class McpServerConfigDto
{
    public string Command { get; set; } = string.Empty;
    public List<string> Args { get; set; } = new();
    public string Type { get; set; } = "stdio";
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string>? Env { get; set; }
    public string? Url { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}

/// <summary>
/// Arguments for the configuration change event.
/// </summary>
public class McpConfigChangedEventArgs : EventArgs
{
    public McpConfigurationDto NewConfiguration { get; }

    public McpConfigChangedEventArgs(McpConfigurationDto newConfiguration)
    {
        NewConfiguration = newConfiguration;
    }
}
