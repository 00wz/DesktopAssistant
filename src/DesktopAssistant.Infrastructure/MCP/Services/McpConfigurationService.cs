using System.Text.Json;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Infrastructure.MCP.Models;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.Infrastructure.MCP.Services;

/// <summary>
/// Сервис управления конфигурацией MCP серверов
/// </summary>
public class McpConfigurationService : IMcpConfigurationService, IDisposable
{
    private readonly ILogger<McpConfigurationService> _logger;
    private readonly FileSystemWatcher? _fileWatcher;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    
    private static readonly string DefaultConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".desktopasst"
    );
    
    public string ConfigFilePath { get; }
    
    public event EventHandler<McpConfigChangedEventArgs>? ConfigurationChanged;
    
    public McpConfigurationService(ILogger<McpConfigurationService> logger)
    {
        _logger = logger;
        ConfigFilePath = Path.Combine(DefaultConfigDirectory, "mcp.json");
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        // Создаём директорию если не существует
        EnsureConfigDirectoryExists();
        
        // Создаём FileWatcher для отслеживания изменений
        try
        {
            _fileWatcher = new FileSystemWatcher(DefaultConfigDirectory, "mcp.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _fileWatcher.Changed += OnConfigFileChanged;
            _logger.LogInformation("FileWatcher created for {ConfigPath}", ConfigFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create FileWatcher for config file");
        }
    }
    
    public async Task<McpConfigurationDto> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(ConfigFilePath))
            {
                _logger.LogInformation("Config file not found, returning empty configuration");
                return new McpConfigurationDto();
            }
            
            var json = await File.ReadAllTextAsync(ConfigFilePath, cancellationToken);
            
            // Обрабатываем пустой файл или файл с пробелами
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogInformation("Config file is empty, returning empty configuration");
                return new McpConfigurationDto();
            }
            
            var config = JsonSerializer.Deserialize<McpConfiguration>(json, _jsonOptions);
            
            return MapToDto(config ?? new McpConfiguration());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load MCP configuration from {Path}", ConfigFilePath);
            return new McpConfigurationDto();
        }
        finally
        {
            _fileLock.Release();
        }
    }
    
    public async Task SaveAsync(McpConfigurationDto config, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            EnsureConfigDirectoryExists();
            
            var model = MapFromDto(config);
            var json = JsonSerializer.Serialize(model, _jsonOptions);
            await File.WriteAllTextAsync(ConfigFilePath, json, cancellationToken);
            
            _logger.LogInformation("MCP configuration saved to {Path}", ConfigFilePath);
        }
        finally
        {
            _fileLock.Release();
        }
    }
    
    public async Task AddServerAsync(string serverId, McpServerConfigDto serverConfig, CancellationToken cancellationToken = default)
    {
        var config = await LoadAsync(cancellationToken);
        config.McpServers[serverId] = serverConfig;
        await SaveAsync(config, cancellationToken);
        
        _logger.LogInformation("Added MCP server '{ServerId}' to configuration", serverId);
    }
    
    public async Task RemoveServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var config = await LoadAsync(cancellationToken);
        if (config.McpServers.Remove(serverId))
        {
            await SaveAsync(config, cancellationToken);
            _logger.LogInformation("Removed MCP server '{ServerId}' from configuration", serverId);
        }
    }
    
    public async Task UpdateServerAsync(string serverId, McpServerConfigDto serverConfig, CancellationToken cancellationToken = default)
    {
        var config = await LoadAsync(cancellationToken);
        config.McpServers[serverId] = serverConfig;
        await SaveAsync(config, cancellationToken);
        
        _logger.LogInformation("Updated MCP server '{ServerId}' in configuration", serverId);
    }
    
    private void EnsureConfigDirectoryExists()
    {
        if (!Directory.Exists(DefaultConfigDirectory))
        {
            Directory.CreateDirectory(DefaultConfigDirectory);
            _logger.LogInformation("Created config directory: {Directory}", DefaultConfigDirectory);
        }
    }
    
    private async void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        _logger.LogDebug("Config file changed: {ChangeType}", e.ChangeType);
        
        // Небольшая задержка чтобы файл успел записаться полностью
        await Task.Delay(100);
        
        try
        {
            var config = await LoadAsync();
            ConfigurationChanged?.Invoke(this, new McpConfigChangedEventArgs(config));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling config file change");
        }
    }
    
    private static McpConfigurationDto MapToDto(McpConfiguration model)
    {
        return new McpConfigurationDto
        {
            McpServers = model.McpServers.ToDictionary(
                kvp => kvp.Key,
                kvp => new McpServerConfigDto
                {
                    Command = kvp.Value.Command,
                    Args = kvp.Value.Args,
                    Type = kvp.Value.Type,
                    Enabled = kvp.Value.Enabled,
                    Env = kvp.Value.Env,
                    Url = kvp.Value.Url,
                    Headers = kvp.Value.Headers
                }
            )
        };
    }
    
    private static McpConfiguration MapFromDto(McpConfigurationDto dto)
    {
        return new McpConfiguration
        {
            McpServers = dto.McpServers.ToDictionary(
                kvp => kvp.Key,
                kvp => new McpServerConfig
                {
                    Command = kvp.Value.Command,
                    Args = kvp.Value.Args,
                    Type = kvp.Value.Type,
                    Enabled = kvp.Value.Enabled,
                    Env = kvp.Value.Env,
                    Url = kvp.Value.Url,
                    Headers = kvp.Value.Headers
                }
            )
        };
    }
    
    public void Dispose()
    {
        _fileWatcher?.Dispose();
        _fileLock.Dispose();
    }
}
