using DesktopAssistant.Application.Interfaces;

namespace DesktopAssistant.Infrastructure.Settings;

/// <summary>
/// Предоставляет актуальный список всех инструментов агента:
/// статических SK-плагинов (CoreTools, McpManagement) и
/// динамически подключённых MCP-tools.
/// </summary>
public class AvailableToolsService : IAvailableToolsProvider, IDisposable
{
    private readonly IMcpServerManager _mcpServerManager;

    /// <summary>
    /// Статические SK-плагины, зарегистрированные в AgentKernelFactory.
    /// При добавлении нового плагина в AgentKernelFactory — обновить этот список.
    /// </summary>
    private static readonly ToolDescriptor[] StaticTools =
    [
        new("CoreTools", "execute_command",        "Выполняет команду в терминале"),
        new("CoreTools", "read_file",              "Читает содержимое файла"),
        new("CoreTools", "write_to_file",          "Записывает содержимое в файл"),
        new("CoreTools", "path_exists",            "Проверяет существование файла или директории"),
        new("CoreTools", "list_directory",         "Возвращает список файлов в директории"),
        new("McpManagement", "search_mcp_servers",      "Поиск MCP серверов в каталоге"),
        new("McpManagement", "fetch_mcp_server_readme", "Загружает README из репозитория MCP сервера"),
        new("McpManagement", "get_mcp_config_path",     "Возвращает путь к файлу конфигурации MCP"),
        new("McpManagement", "get_mcp_servers_directory","Возвращает путь для клонирования MCP серверов"),
        new("McpManagement", "add_mcp_server",          "Добавляет MCP сервер в конфигурацию"),
    ];

    public event EventHandler? ToolsChanged;

    public AvailableToolsService(IMcpServerManager mcpServerManager)
    {
        _mcpServerManager = mcpServerManager;
        _mcpServerManager.ServerChanged += OnServerChanged;
    }

    public IReadOnlyList<ToolDescriptor> GetAvailableTools()
    {
        var mcpTools = _mcpServerManager.GetAllTools()
            .Select(t => new ToolDescriptor(t.ServerId, t.Name, t.Description));

        return StaticTools.Concat(mcpTools).ToList();
    }

    private void OnServerChanged(object? sender, McpServerChangedEventArgs e)
    {
        if (e.NewStatus is McpServerStatusDto.Connected or McpServerStatusDto.Disconnected)
            ToolsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _mcpServerManager.ServerChanged -= OnServerChanged;
    }
}
