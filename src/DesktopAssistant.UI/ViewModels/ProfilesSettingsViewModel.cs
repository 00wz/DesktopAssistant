using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel для секции управления профилями ассистента в панели настроек.
/// Содержит список профилей и управляет inline-редактором.
/// </summary>
public partial class ProfilesSettingsViewModel : ObservableObject
{
    private readonly IAssistantProfileService _profileService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProfilesSettingsViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<AssistantProfileDto> _profiles = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorVisible))]
    private ProfileEditorViewModel? _activeEditor;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLoading;

    public bool IsEditorVisible => ActiveEditor != null;

    public ProfilesSettingsViewModel(
        IAssistantProfileService profileService,
        IServiceProvider serviceProvider,
        ILogger<ProfilesSettingsViewModel> logger)
    {
        _profileService = profileService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    [RelayCommand]
    public async Task LoadProfilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            var profiles = await _profileService.GetAssistantProfilesAsync(cancellationToken);
            Profiles = new ObservableCollection<AssistantProfileDto>(profiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading assistant profiles");
            ErrorMessage = $"Ошибка загрузки профилей: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void StartCreate()
    {
        var editor = _serviceProvider.GetRequiredService<ProfileEditorViewModel>();
        editor.InitCreate();
        editor.OnSaved = async _ =>
        {
            await LoadProfilesAsync();
            ActiveEditor = null;
        };
        editor.OnCancelled = () => ActiveEditor = null;
        ActiveEditor = editor;
    }

    [RelayCommand]
    private void StartEdit(AssistantProfileDto profile)
    {
        var editor = _serviceProvider.GetRequiredService<ProfileEditorViewModel>();
        editor.InitEdit(profile);
        editor.OnSaved = async _ =>
        {
            await LoadProfilesAsync();
            ActiveEditor = null;
        };
        editor.OnCancelled = () => ActiveEditor = null;
        ActiveEditor = editor;
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(AssistantProfileDto profile)
    {
        try
        {
            ErrorMessage = null;
            await _profileService.DeleteAssistantProfileAsync(profile.Id);
            await LoadProfilesAsync();
            ActiveEditor = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting profile {ProfileId}", profile.Id);
            ErrorMessage = $"Ошибка удаления профиля: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SetDefaultAsync(AssistantProfileDto profile)
    {
        try
        {
            ErrorMessage = null;
            await _profileService.SetDefaultAssistantProfileAsync(profile.Id);
            await LoadProfilesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting default profile {ProfileId}", profile.Id);
            ErrorMessage = $"Ошибка установки профиля по умолчанию: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SetSummarizationAsync(AssistantProfileDto profile)
    {
        try
        {
            ErrorMessage = null;
            await _profileService.SetSummarizationProfileAsync(profile.Id);
            await LoadProfilesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting summarization profile {ProfileId}", profile.Id);
            ErrorMessage = $"Ошибка установки профиля суммаризации: {ex.Message}";
        }
    }
}
