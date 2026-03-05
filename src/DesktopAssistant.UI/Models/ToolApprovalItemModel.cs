using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Application.Interfaces;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// Модель одного tool для отображения в настройках auto-approve.
/// При изменении IsAutoApproved автоматически сохраняет настройку в БД.
/// </summary>
public partial class ToolApprovalItemModel : ObservableObject
{
    private readonly IToolApprovalService _toolApprovalService;
    private bool _isInitializing;

    public string PluginName { get; }
    public string FunctionName { get; }
    public string? Description { get; }
    public string DisplayName => $"{PluginName}.{FunctionName}";

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
    /// Устанавливает начальное значение без записи в БД.
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
