using System.Collections.ObjectModel;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// Группа tools одного плагина/MCP-сервера для отображения в настройках auto-approve.
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
