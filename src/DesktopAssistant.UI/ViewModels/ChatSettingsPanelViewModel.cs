using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.UI.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel for the conversation settings panel.
/// Stores draft values for all settings and applies them with a single Save command.
/// </summary>
public partial class ChatSettingsPanelViewModel : ObservableObject
{
    private readonly IAssistantProfileService _profileService;
    private readonly ILogger<ChatSettingsPanelViewModel> _logger;

    private IConversationSession? _session;

    // Snapshot of values at load time — used to determine HasChanges
    private string _originalSystemPrompt = string.Empty;
    private Guid? _originalProfileId;
    private ConversationMode _originalMode = ConversationMode.Chat;

    /// <summary>Called after a successful save; passes the new profile name.</summary>
    public Action<string>? OnProfileApplied { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _systemPrompt = string.Empty;

    [ObservableProperty]
    private ObservableCollection<AssistantProfileDto> _availableProfiles = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private AssistantProfileDto? _selectedProfile;

    public IReadOnlyList<ConversationModeOption> AvailableModes { get; } =
    [
        new(ConversationMode.Chat, "Chat"),
        new(ConversationMode.Agent, "Agent"),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private ConversationModeOption _selectedModeOption;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>True for a brief period after a successful save.</summary>
    [ObservableProperty]
    private bool _isSaved;

    public bool HasChanges =>
        SystemPrompt != _originalSystemPrompt ||
        SelectedProfile?.Id != _originalProfileId ||
        SelectedModeOption.Mode != _originalMode;

    public ChatSettingsPanelViewModel(
        IAssistantProfileService profileService,
        ILogger<ChatSettingsPanelViewModel> logger)
    {
        _profileService = profileService;
        _logger = logger;
        _selectedModeOption = AvailableModes[0]; // Chat by default
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
            SelectedModeOption = AvailableModes.FirstOrDefault(m => m.Mode == settings.Mode)
                ?? AvailableModes[0];

            TakeSnapshot();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading conversation settings");
            ErrorMessage = $"Error loading settings: {ex.Message}";
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

            await _session.ChangeModeAsync(SelectedModeOption.Mode);

            TakeSnapshot();
            IsSaved = true;
            _ = ResetSavedFeedbackAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving conversation settings");
            ErrorMessage = $"Error saving settings: {ex.Message}";
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
        _originalMode = SelectedModeOption.Mode;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private async Task ResetSavedFeedbackAsync()
    {
        await Task.Delay(2000);
        IsSaved = false;
    }
}
