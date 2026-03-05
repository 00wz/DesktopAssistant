using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.UI.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel секции «Автоподтверждение tools» в панели настроек.
/// Отображает список всех доступных tools с возможностью включения auto-approve.
/// Динамически обновляется при подключении/отключении MCP серверов.
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

    public ObservableCollection<ToolApprovalItemModel> Tools { get; } = new();

    public bool HasTools => Tools.Count > 0;

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

            var tools = _availableToolsProvider.GetAvailableTools();
            Tools.Clear();

            foreach (var descriptor in tools)
            {
                var approved = await _toolApprovalService.IsAutoApprovedAsync(
                    descriptor.PluginName, descriptor.FunctionName);

                var item = new ToolApprovalItemModel(
                    descriptor.PluginName,
                    descriptor.FunctionName,
                    descriptor.Description,
                    _toolApprovalService);
                item.InitializeValue(approved);
                Tools.Add(item);
            }

            OnPropertyChanged(nameof(HasTools));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading tool approval settings");
            ErrorMessage = $"Ошибка загрузки настроек: {ex.Message}";
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
