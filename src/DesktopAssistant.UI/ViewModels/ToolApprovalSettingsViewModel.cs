using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.UI.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel for the "Tool Auto-Approval" section of the settings panel.
/// Displays tools grouped by plugin/MCP server.
/// Dynamically updates when MCP servers connect or disconnect.
/// </summary>
public partial class ToolApprovalSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IToolApprovalService _toolApprovalService;
    private readonly IAvailableToolsProvider _availableToolsProvider;
    private readonly ILogger<ToolApprovalSettingsViewModel> _logger;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<ToolApprovalGroupModel> Groups { get; } = new();

    public bool HasGroups => Groups.Count > 0;

    public ToolApprovalSettingsViewModel(
        IToolApprovalService toolApprovalService,
        IAvailableToolsProvider availableToolsProvider,
        ILogger<ToolApprovalSettingsViewModel> logger)
    {
        _toolApprovalService = toolApprovalService;
        _availableToolsProvider = availableToolsProvider;
        _logger = logger;

        _availableToolsProvider.ToolsChanged += OnToolsChanged;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var descriptors = _availableToolsProvider.GetAvailableTools();
            Groups.Clear();

            foreach (var grouping in descriptors.GroupBy(t => t.PluginName))
            {
                var group = new ToolApprovalGroupModel(grouping.Key);

                foreach (var descriptor in grouping)
                {
                    var approved = await _toolApprovalService.IsAutoApprovedAsync(
                        descriptor.PluginName, descriptor.FunctionName);

                    var item = new ToolApprovalItemModel(
                        descriptor.PluginName,
                        descriptor.FunctionName,
                        descriptor.Description,
                        _toolApprovalService);
                    item.InitializeValue(approved);
                    group.Tools.Add(item);
                }

                Groups.Add(group);
            }

            OnPropertyChanged(nameof(HasGroups));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading tool approval settings");
            ErrorMessage = $"Error loading settings: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnToolsChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing tool list after server change");
            }
        });
    }

    public void Dispose()
    {
        _availableToolsProvider.ToolsChanged -= OnToolsChanged;
    }
}
