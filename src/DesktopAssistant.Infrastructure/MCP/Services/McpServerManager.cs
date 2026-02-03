using System.Collections.Concurrent;
using System.Text.Json;
using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace DesktopAssistant.Infrastructure.MCP.Services;

/// <summary>
/// Менеджер MCP серверов - управляет подключениями и вызовом tools
/// </summary>
public class McpServerManager : IMcpServerManager, IAsyncDisposable
{
    private readonly ILogger<McpServerManager> _logger;
    private readonly IMcpConfigurationService _configService;
    private readonly ConcurrentDictionary<string, IMcpClient> _clients = new();
    private readonly ConcurrentDictionary<string, McpServerInfoDto> _serverInfos = new();
    
    public event EventHandler<McpServerChangedEventArgs>? ServerChanged;
    
    public McpServerManager(
        ILogger<McpServerManager> logger,
        IMcpConfigurationService configService)
    {
        _logger = logger;
        _configService = configService;
        
        // Подписываемся на изменения конфигурации
        _configService.ConfigurationChanged += OnConfigurationChanged;
    }
    
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing MCP Server Manager...");
        
        var config = await _configService.LoadAsync(cancellationToken);
        
        foreach (var (serverId, serverConfig) in config.McpServers)
        {
            if (serverConfig.Enabled)
            {
                await ConnectServerAsync(serverId, cancellationToken);
            }
            else
            {
                _serverInfos[serverId] = new McpServerInfoDto
                {
                    Id = serverId,
                    Status = McpServerStatusDto.Disconnected
                };
            }
        }
        
        _logger.LogInformation("MCP Server Manager initialized. Connected servers: {Count}", 
            _clients.Count);
    }
    
    public async Task ConnectServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to MCP server '{ServerId}'...", serverId);
        
        // Обновляем статус
        UpdateServerStatus(serverId, McpServerStatusDto.Connecting);
        
        try
        {
            var config = await _configService.LoadAsync(cancellationToken);
            
            if (!config.McpServers.TryGetValue(serverId, out var serverConfig))
            {
                throw new InvalidOperationException($"Server '{serverId}' not found in configuration");
            }
            
            // Отключаем старое подключение если есть
            if (_clients.TryRemove(serverId, out var oldClient))
            {
                await oldClient.DisposeAsync();
            }
            
            // Создаём транспорт и клиент через McpClientFactory
            var transportOptions = new StdioClientTransportOptions
            {
                Name = serverId,
                Command = serverConfig.Command,
                Arguments = serverConfig.Args
            };
            
            if (serverConfig.Env != null)
            {
                transportOptions.EnvironmentVariables = serverConfig.Env;
            }
            
            var transport = new StdioClientTransport(transportOptions);
            var client = await McpClientFactory.CreateAsync(transport, cancellationToken: cancellationToken);
            
            // Получаем список tools (ListToolsAsync возвращает IList<McpClientTool>)
            var mcpTools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            var tools = mcpTools.Select(t => new McpToolInfoDto
            {
                ServerId = serverId,
                Name = t.Name,
                Description = t.Description,
                InputSchema = t.JsonSchema.ValueKind != JsonValueKind.Undefined
                    ? t.JsonSchema
                    : null
            }).ToList();
            
            // Сохраняем клиент и информацию
            _clients[serverId] = client;
            _serverInfos[serverId] = new McpServerInfoDto
            {
                Id = serverId,
                Status = McpServerStatusDto.Connected,
                Tools = tools
            };
            
            UpdateServerStatus(serverId, McpServerStatusDto.Connected);
            
            _logger.LogInformation("Connected to MCP server '{ServerId}' with {ToolCount} tools", 
                serverId, tools.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to MCP server '{ServerId}'", serverId);
            
            _serverInfos[serverId] = new McpServerInfoDto
            {
                Id = serverId,
                Status = McpServerStatusDto.Error,
                ErrorMessage = ex.Message
            };
            
            UpdateServerStatus(serverId, McpServerStatusDto.Error, ex.Message);
        }
    }
    
    public async Task DisconnectServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnecting from MCP server '{ServerId}'...", serverId);
        
        if (_clients.TryRemove(serverId, out var client))
        {
            await client.DisposeAsync();
        }
        
        if (_serverInfos.TryGetValue(serverId, out var info))
        {
            info.Status = McpServerStatusDto.Disconnected;
            info.Tools.Clear();
        }
        
        UpdateServerStatus(serverId, McpServerStatusDto.Disconnected);
    }
    
    public async Task RestartServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        await DisconnectServerAsync(serverId, cancellationToken);
        await ConnectServerAsync(serverId, cancellationToken);
    }
    
    public IReadOnlyList<McpServerInfoDto> GetConnectedServers()
    {
        return _serverInfos.Values
            .Where(s => s.Status == McpServerStatusDto.Connected)
            .ToList();
    }
    
    public McpServerInfoDto? GetServerInfo(string serverId)
    {
        return _serverInfos.TryGetValue(serverId, out var info) ? info : null;
    }
    
    public IReadOnlyList<McpToolInfoDto> GetAllTools()
    {
        return _serverInfos.Values
            .Where(s => s.Status == McpServerStatusDto.Connected)
            .SelectMany(s => s.Tools)
            .ToList();
    }
    
    public async Task<McpToolResultDto> InvokeToolAsync(
        string serverId, 
        string toolName, 
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Invoking tool '{ToolName}' on server '{ServerId}'", toolName, serverId);
        
        if (!_clients.TryGetValue(serverId, out var client))
        {
            return McpToolResultDto.Error($"Server '{serverId}' is not connected");
        }
        
        try
        {
            // Преобразуем JsonElement в Dictionary для MCP SDK
            var argsDict = new Dictionary<string, object?>();
            if (arguments.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in arguments.EnumerateObject())
                {
                    argsDict[prop.Name] = ConvertJsonElement(prop.Value);
                }
            }
            
            var result = await client.CallToolAsync(
                toolName, 
                argsDict,
                cancellationToken: cancellationToken);
            
            // Собираем текстовый результат
            var content = string.Join("\n", result.Content
                .Where(c => c.Type == "text")
                .Select(c => c.Text));
            
            if (result.IsError)
            {
                return McpToolResultDto.Error(content);
            }
            
            return McpToolResultDto.Success(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invoking tool '{ToolName}' on server '{ServerId}'", 
                toolName, serverId);
            return McpToolResultDto.Error(ex.Message);
        }
    }
    
    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => element.GetRawText()
        };
    }
    
    private void UpdateServerStatus(string serverId, McpServerStatusDto status, string? errorMessage = null)
    {
        ServerChanged?.Invoke(this, new McpServerChangedEventArgs(serverId, status, errorMessage));
    }
    
    private async void OnConfigurationChanged(object? sender, McpConfigChangedEventArgs e)
    {
        _logger.LogInformation("MCP configuration changed, reloading servers...");
        
        // Определяем какие серверы добавить/удалить/обновить
        var currentServers = _serverInfos.Keys.ToHashSet();
        var newServers = e.NewConfiguration.McpServers.Keys.ToHashSet();
        
        // Удаляем серверы которых больше нет в конфигурации
        var toRemove = currentServers.Except(newServers);
        foreach (var serverId in toRemove)
        {
            await DisconnectServerAsync(serverId);
            _serverInfos.TryRemove(serverId, out _);
        }
        
        // Добавляем/обновляем серверы
        foreach (var (serverId, serverConfig) in e.NewConfiguration.McpServers)
        {
            if (serverConfig.Enabled)
            {
                // Переподключаем если конфигурация изменилась
                await RestartServerAsync(serverId);
            }
            else if (_clients.ContainsKey(serverId))
            {
                await DisconnectServerAsync(serverId);
            }
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        _configService.ConfigurationChanged -= OnConfigurationChanged;
        
        foreach (var (serverId, client) in _clients)
        {
            _logger.LogDebug("Disposing MCP client for '{ServerId}'", serverId);
            await client.DisposeAsync();
        }
        
        _clients.Clear();
        _serverInfos.Clear();
    }
}
