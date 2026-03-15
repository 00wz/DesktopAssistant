using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel for the inline new conversation creation panel.
/// Displayed in the main window instead of the empty screen or TabControl.
/// </summary>
public partial class NewConversationPanelViewModel : ObservableObject
{
    private readonly IAssistantProfileService _profileService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NewConversationPanelViewModel> _logger;

    /// <summary>Called when the user confirms conversation creation.</summary>
    public Func<NewConversationParams, Task>? OnConfirm { get; set; }

    /// <summary>Called when the user cancels.</summary>
    public Action? OnCancel { get; set; }

    [ObservableProperty]
    private ObservableCollection<AssistantProfileDto> _availableProfiles = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private AssistantProfileDto? _selectedProfile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _firstMessage = string.Empty;

    [ObservableProperty]
    private string _systemPrompt = string.Empty;

    /// <summary>Inline profile editor (null when the form is hidden).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreateProfileMode))]
    private ProfileEditorViewModel? _inlineProfileEditor;

    /// <summary>True if the profile creation form is visible.</summary>
    public bool IsCreateProfileMode => InlineProfileEditor != null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public NewConversationPanelViewModel(
        IAssistantProfileService profileService,
        IServiceProvider serviceProvider,
        ILogger<NewConversationPanelViewModel> logger)
    {
        _profileService = profileService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task LoadProfilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var profiles = await _profileService.GetAssistantProfilesAsync(cancellationToken);
            AvailableProfiles = new ObservableCollection<AssistantProfileDto>(profiles);

            SelectedProfile = AvailableProfiles.FirstOrDefault(p => p.IsDefault)
                ?? AvailableProfiles.FirstOrDefault();

            // If no profiles exist — immediately open the creation form
            if (AvailableProfiles.Count == 0)
                ToggleCreateProfileMode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading assistant profiles");
            ErrorMessage = $"Error loading profiles: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ToggleCreateProfileMode()
    {
        if (InlineProfileEditor != null)
        {
            InlineProfileEditor = null;
            return;
        }

        var editor = _serviceProvider.GetRequiredService<ProfileEditorViewModel>();
        editor.InitCreate();
        editor.OnSaved = async dto =>
        {
            await LoadProfilesAsync();
            SelectedProfile = AvailableProfiles.FirstOrDefault(p => p.Id == dto.Id)
                              ?? AvailableProfiles.FirstOrDefault();
            InlineProfileEditor = null;
        };
        editor.OnCancelled = () => InlineProfileEditor = null;
        InlineProfileEditor = editor;
    }

    private bool CanConfirm() => SelectedProfile != null && !IsLoading && !string.IsNullOrWhiteSpace(FirstMessage);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (SelectedProfile == null || OnConfirm == null) return;

        var trimmedMessage = FirstMessage.Trim();
        const int maxTitleLength = 50;
        var title = trimmedMessage.Length <= maxTitleLength
            ? trimmedMessage
            : trimmedMessage[..maxTitleLength] + "…";

        var parameters = new NewConversationParams(
            title,
            SelectedProfile.Id,
            SystemPrompt.Trim(),
            trimmedMessage);

        await OnConfirm(parameters);
    }

    [RelayCommand]
    private void Cancel()
    {
        OnCancel?.Invoke();
    }
}

/// <summary>Parameters for creating a new conversation, returned from the panel.</summary>
public record NewConversationParams(string Title, Guid AssistantProfileId, string SystemPrompt, string FirstMessage);
