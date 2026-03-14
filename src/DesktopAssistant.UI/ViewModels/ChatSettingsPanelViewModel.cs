using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel панели настроек диалога.
/// Хранит черновые значения всех настроек и применяет их единой командой Save.
/// </summary>
public partial class ChatSettingsPanelViewModel : ObservableObject
{
    private readonly IAssistantProfileService _profileService;
    private readonly ILogger<ChatSettingsPanelViewModel> _logger;

    private IConversationSession? _session;

    // Снапшот значений на момент загрузки — используется для определения HasChanges
    private string _originalSystemPrompt = string.Empty;
    private Guid? _originalProfileId;

    /// <summary>Вызывается после успешного сохранения, передаёт новое имя профиля.</summary>
    public Action<string>? OnProfileApplied { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _systemPrompt = string.Empty;

    [ObservableProperty]
    private ObservableCollection<AssistantProfileDto> _availableProfiles = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private AssistantProfileDto? _selectedProfile;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>True в течение короткого времени после успешного сохранения.</summary>
    [ObservableProperty]
    private bool _isSaved;

    public bool HasChanges =>
        SystemPrompt != _originalSystemPrompt ||
        SelectedProfile?.Id != _originalProfileId;

    public ChatSettingsPanelViewModel(
        IAssistantProfileService profileService,
        ILogger<ChatSettingsPanelViewModel> logger)
    {
        _profileService = profileService;
        _logger = logger;
    }

    public async Task LoadAsync(IConversationSession session, CancellationToken ct = default)
    {
        _session = session;
        IsLoading = true;
        IsSaved = false;
        ErrorMessage = null;

        try
        {
            var settings = await session.GetSettingsAsync(ct);
            if (settings == null) return;

            var profiles = await _profileService.GetAssistantProfilesAsync(ct);
            AvailableProfiles = new ObservableCollection<AssistantProfileDto>(profiles);

            SystemPrompt = settings.SystemPrompt;
            SelectedProfile = settings.AssistantProfileId.HasValue
                ? AvailableProfiles.FirstOrDefault(p => p.Id == settings.AssistantProfileId.Value)
                : null;

            TakeSnapshot();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading conversation settings");
            ErrorMessage = $"Ошибка загрузки настроек: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanSave() => HasChanges && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_session == null)
            throw new InvalidOperationException("Session is not loaded");

        IsLoading = true;
        IsSaved = false;
        ErrorMessage = null;

        try
        {
            await _session.UpdateSystemPromptAsync(SystemPrompt);

            if (SelectedProfile != null)
            {
                await _session.ChangeProfileAsync(SelectedProfile.Id);
                OnProfileApplied?.Invoke(SelectedProfile.ModelId);
            }

            TakeSnapshot();
            IsSaved = true;
            _ = ResetSavedFeedbackAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving conversation settings");
            ErrorMessage = $"Ошибка сохранения настроек: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void TakeSnapshot()
    {
        _originalSystemPrompt = SystemPrompt;
        _originalProfileId = SelectedProfile?.Id;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private async Task ResetSavedFeedbackAsync()
    {
        await Task.Delay(2000);
        IsSaved = false;
    }
}
