namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Сервис для управления конфигурацией MCP серверов
/// </summary>
public interface IMcpConfigurationService
{
    /// <summary>
    /// Путь к файлу конфигурации mcp.json
    /// </summary>
    string ConfigFilePath { get; }
    
    /// <summary>
    /// Загружает конфигурацию из файла
    /// </summary>
    Task<McpConfigurationDto> LoadAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Сохраняет конфигурацию в файл
    /// </summary>
    Task SaveAsync(McpConfigurationDto config, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Добавляет новый сервер в конфигурацию
    /// </summary>
    Task AddServerAsync(string serverId, McpServerConfigDto serverConfig, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Удаляет сервер из конфигурации
    /// </summary>
    Task RemoveServerAsync(string serverId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Обновляет конфигурацию сервера
    /// </summary>
    Task UpdateServerAsync(string serverId, McpServerConfigDto serverConfig, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Событие изменения конфигурации (от FileWatcher)
    /// </summary>
    event EventHandler<McpConfigChangedEventArgs>? ConfigurationChanged;
}

/// <summary>
/// DTO конфигурации MCP
/// </summary>
public class McpConfigurationDto
{
    public Dictionary<string, McpServerConfigDto> McpServers { get; set; } = new();
}

/// <summary>
/// DTO конфигурации одного MCP сервера
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
/// Аргументы события изменения конфигурации
/// </summary>
public class McpConfigChangedEventArgs : EventArgs
{
    public McpConfigurationDto NewConfiguration { get; }
    
    public McpConfigChangedEventArgs(McpConfigurationDto newConfiguration)
    {
        NewConfiguration = newConfiguration;
    }
}
