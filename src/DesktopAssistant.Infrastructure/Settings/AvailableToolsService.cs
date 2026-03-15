using DesktopAssistant.Application.Interfaces;

namespace DesktopAssistant.Infrastructure.Settings;

/// <summary>
/// Provides the current list of all agent tools:
/// static SK plugins (CoreTools, McpManagement) and
/// dynamically connected MCP tools.
/// </summary>
public class AvailableToolsService : IAvailableToolsProvider, IDisposable
{
    private readonly IMcpServerManager _mcpServerManager;

    /// <summary>
    /// Static SK plugins registered in AgentKernelFactory.
    /// When adding a new plugin to AgentKernelFactory, update this list as well.
    /// </summary>
    private static readonly ToolDescriptor[] StaticTools =
    [
        new("CoreTools", "execute_command",        "Executes a command in the terminal"),
        new("CoreTools", "read_file",              "Reads the contents of a file"),
        new("CoreTools", "write_to_file",          "Writes content to a file"),
        new("CoreTools", "path_exists",            "Checks whether a file or directory exists"),
        new("CoreTools", "list_directory",         "Returns a list of files in a directory"),
        new("McpManagement", "search_mcp_servers",      "Search for MCP servers in the catalog"),
        new("McpManagement", "fetch_mcp_server_readme", "Fetches the README from an MCP server repository"),
        new("McpManagement", "get_mcp_config_path",     "Returns the path to the MCP configuration file"),
        new("McpManagement", "get_mcp_servers_directory","Returns the path for cloning MCP servers"),
        new("McpManagement", "add_mcp_server",          "Adds an MCP server to the configuration"),
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
