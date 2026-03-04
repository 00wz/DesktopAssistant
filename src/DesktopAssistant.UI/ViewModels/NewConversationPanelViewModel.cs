using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel для inline-панели создания нового диалога.
/// Отображается в главном окне вместо пустого экрана или TabControl.
/// </summary>
public partial class NewConversationPanelViewModel : ObservableObject
{
    private readonly IAssistantProfileService _profileService;
    private readonly IServiceProvider _serviceProvider;
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

    /// <summary>Inline-редактор профиля (null если форма скрыта).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreateProfileMode))]
    private ProfileEditorViewModel? _inlineProfileEditor;

    /// <summary>True если форма создания профиля видима.</summary>
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

            // Если профилей нет — сразу открываем форму создания
            if (AvailableProfiles.Count == 0)
                ToggleCreateProfileMode();
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
