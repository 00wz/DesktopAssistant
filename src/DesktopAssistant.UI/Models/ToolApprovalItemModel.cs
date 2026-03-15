using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Application.Interfaces;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// Model for a single tool displayed in the auto-approve settings.
/// When IsAutoApproved changes, the setting is automatically persisted to the database.
/// </summary>
public partial class ToolApprovalItemModel : ObservableObject
{
    private readonly IToolApprovalService _toolApprovalService;
    private bool _isInitializing;

    public string PluginName { get; }
    public string FunctionName { get; }
    public string? Description { get; }
    /// <summary>Displayed inside the group — only the function name (the plugin name is the group header).</summary>
    public string DisplayName => FunctionName;

    [ObservableProperty]
    private bool _isAutoApproved;

    public ToolApprovalItemModel(
        string pluginName,
        string functionName,
        string? description,
        IToolApprovalService toolApprovalService)
    {
        PluginName = pluginName;
        FunctionName = functionName;
        Description = description;
        _toolApprovalService = toolApprovalService;
    }

    /// <summary>
    /// Sets the initial value without writing to the database.
    /// </summary>
    internal void InitializeValue(bool value)
    {
        _isInitializing = true;
        IsAutoApproved = value;
        _isInitializing = false;
    }

    partial void OnIsAutoApprovedChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _toolApprovalService.SetAutoApprovedAsync(PluginName, FunctionName, value);
    }
}
