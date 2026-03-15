using System.Collections.ObjectModel;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// Group of tools for a single plugin/MCP server, displayed in the auto-approve settings.
/// </summary>
public class ToolApprovalGroupModel
{
    public string PluginName { get; }
    public ObservableCollection<ToolApprovalItemModel> Tools { get; } = new();

    public ToolApprovalGroupModel(string pluginName)
    {
        PluginName = pluginName;
    }
}
