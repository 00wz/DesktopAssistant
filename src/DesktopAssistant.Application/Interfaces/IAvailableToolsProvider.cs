namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Descriptor of an available tool (SK plugin or MCP tool).
/// </summary>
public record ToolDescriptor(string PluginName, string FunctionName, string? Description);

/// <summary>
/// Provides the current list of all tools available to the agent:
/// static SK plugins and dynamically connected MCP tools.
/// Raises the <see cref="ToolsChanged"/> event when the set of tools changes.
/// </summary>
public interface IAvailableToolsProvider
{
    /// <summary>
    /// Returns the current list of all available tools.
    /// </summary>
    IReadOnlyList<ToolDescriptor> GetAvailableTools();

    /// <summary>
    /// Raised when tools are added or removed (e.g. an MCP server connects or disconnects).
    /// </summary>
    event EventHandler? ToolsChanged;
}
