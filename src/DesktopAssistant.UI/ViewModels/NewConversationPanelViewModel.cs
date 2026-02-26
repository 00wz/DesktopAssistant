using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel для inline-панели создания нового диалога.
/// Отображается в главном окне вместо пустого экрана или TabControl.
/// </summary>
public partial class NewConversationPanelViewModel : ObservableObject
{
    private readonly IChatService _chatService;
    private readonly ILogger<NewConversationPanelViewModel> _logger;

    /// <summary>Вызывается при подтверждении создания диалога.</summary>
    public Func<NewConversationParams, Task>? OnConfirm { get; set; }

    /// <summary>Вызывается при отмене.</summary>
    public Action? OnCancel { get; set; }

    [ObservableProperty]
    private ObservableCollection<AssistantProfileDto> _availableProfiles = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private AssistantProfileDto? _selectedProfile;

    [ObservableProperty]
    private string _conversationTitle = "Новый чат";

    [ObservableProperty]
    private string _systemPrompt = string.Empty;

    [ObservableProperty]
    private bool _isCreateProfileMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    // ── Поля нового профиля ──────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))]
    private string _newProfileBaseUrl = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))]
    private string _newProfileModelId = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))]
    private string _newProfileApiKey = string.Empty;

    [ObservableProperty]
    private double _newProfileTemperature = 0.7;

    [ObservableProperty]
    private int _newProfileMaxTokens = 4096;

    [ObservableProperty]
    private bool _newProfileIsDefault;

    public NewConversationPanelViewModel(
        IChatService chatService,
        ILogger<NewConversationPanelViewModel> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    public async Task LoadProfilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var profiles = await _chatService.GetAssistantProfilesAsync(cancellationToken);
            AvailableProfiles = new ObservableCollection<AssistantProfileDto>(profiles);

            SelectedProfile = AvailableProfiles.FirstOrDefault(p => p.IsDefault)
                ?? AvailableProfiles.FirstOrDefault();

            // Если профилей нет — сразу открываем форму создания
            if (AvailableProfiles.Count == 0)
                IsCreateProfileMode = true;
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
    private void ToggleCreateProfileMode()
    {
        IsCreateProfileMode = !IsCreateProfileMode;
        ErrorMessage = null;
    }

    private bool CanCreateProfile() =>
        !string.IsNullOrWhiteSpace(NewProfileName) &&
        !string.IsNullOrWhiteSpace(NewProfileBaseUrl) &&
        !string.IsNullOrWhiteSpace(NewProfileModelId) &&
        !string.IsNullOrWhiteSpace(NewProfileApiKey) &&
        !IsLoading;

    [RelayCommand(CanExecute = nameof(CanCreateProfile))]
    private async Task CreateProfileAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var profile = await _chatService.CreateAssistantProfileAsync(
                NewProfileName.Trim(),
                NewProfileBaseUrl.Trim(),
                NewProfileModelId.Trim(),
                NewProfileApiKey.Trim(),
                NewProfileTemperature,
                NewProfileMaxTokens,
                NewProfileIsDefault || AvailableProfiles.Count == 0);

            AvailableProfiles.Add(profile);
            SelectedProfile = profile;
            IsCreateProfileMode = false;

            // Сбрасываем поля формы
            NewProfileName = string.Empty;
            NewProfileBaseUrl = string.Empty;
            NewProfileModelId = string.Empty;
            NewProfileApiKey = string.Empty;
            NewProfileTemperature = 0.7;
            NewProfileMaxTokens = 4096;
            NewProfileIsDefault = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating assistant profile");
            ErrorMessage = $"Ошибка создания профиля: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnNewProfileNameChanged(string value) => CreateProfileCommand.NotifyCanExecuteChanged();
    partial void OnNewProfileBaseUrlChanged(string value) => CreateProfileCommand.NotifyCanExecuteChanged();
    partial void OnNewProfileModelIdChanged(string value) => CreateProfileCommand.NotifyCanExecuteChanged();
    partial void OnNewProfileApiKeyChanged(string value) => CreateProfileCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadingChanged(bool value) => CreateProfileCommand.NotifyCanExecuteChanged();

    private bool CanConfirm() => SelectedProfile != null && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (SelectedProfile == null || OnConfirm == null) return;

        var parameters = new NewConversationParams(
            ConversationTitle.Trim().Length > 0 ? ConversationTitle.Trim() : "Новый чат",
            SelectedProfile.Id,
            SystemPrompt.Trim());

        await OnConfirm(parameters);
    }

    [RelayCommand]
    private void Cancel()
    {
        OnCancel?.Invoke();
    }
}

/// <summary>Параметры для создания нового диалога, возвращаемые из панели.</summary>
public record NewConversationParams(string Title, Guid AssistantProfileId, string SystemPrompt);
