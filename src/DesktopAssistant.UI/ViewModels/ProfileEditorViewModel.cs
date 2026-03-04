using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// Переиспользуемый ViewModel для создания и редактирования профиля ассистента.
/// Перед отображением вызовите InitCreate() или InitEdit(dto).
/// </summary>
public partial class ProfileEditorViewModel : ObservableObject
{
    private readonly IChatService _chatService;
    private readonly ILogger<ProfileEditorViewModel> _logger;

    private Guid? _editingProfileId;

    /// <summary>Вызывается после успешного сохранения. Получает сохранённый DTO.</summary>
    public Func<AssistantProfileDto, Task>? OnSaved { get; set; }

    /// <summary>Вызывается при отмене.</summary>
    public Action? OnCancelled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiKeyWatermark))]
    private bool _isEditMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _modelId = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private double _temperature = 0.7;

    [ObservableProperty]
    private int _maxTokens = 4096;

    [ObservableProperty]
    private bool _isDefault;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Подсказка для поля API Key: зависит от режима.</summary>
    public string ApiKeyWatermark => IsEditMode
        ? "Оставьте пустым, чтобы сохранить текущий ключ"
        : "sk-...";

    public ProfileEditorViewModel(IChatService chatService, ILogger<ProfileEditorViewModel> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    /// <summary>Сбрасывает форму для создания нового профиля.</summary>
    public void InitCreate()
    {
        _editingProfileId = null;
        IsEditMode = false;
        Name = string.Empty;
        BaseUrl = string.Empty;
        ModelId = string.Empty;
        ApiKey = string.Empty;
        Temperature = 0.7;
        MaxTokens = 4096;
        IsDefault = false;
        ErrorMessage = null;
        IsLoading = false;
    }

    /// <summary>Заполняет форму данными существующего профиля для редактирования.</summary>
    public void InitEdit(AssistantProfileDto profile)
    {
        _editingProfileId = profile.Id;
        IsEditMode = true;
        Name = profile.Name;
        BaseUrl = profile.BaseUrl;
        ModelId = profile.ModelId;
        ApiKey = string.Empty;
        Temperature = profile.Temperature;
        MaxTokens = profile.MaxTokens;
        IsDefault = profile.IsDefault;
        ErrorMessage = null;
        IsLoading = false;
    }

    private bool CanSave() =>
        !IsLoading &&
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(ModelId) &&
        (IsEditMode || !string.IsNullOrWhiteSpace(ApiKey));

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            AssistantProfileDto saved;

            if (IsEditMode && _editingProfileId.HasValue)
            {
                await _chatService.UpdateAssistantProfileAsync(
                    _editingProfileId.Value,
                    Name.Trim(), BaseUrl.Trim(), ModelId.Trim(),
                    Temperature, MaxTokens);

                if (!string.IsNullOrWhiteSpace(ApiKey))
                    await _chatService.SetAssistantProfileApiKeyAsync(_editingProfileId.Value, ApiKey.Trim());

                if (IsDefault)
                    await _chatService.SetDefaultAssistantProfileAsync(_editingProfileId.Value);

                var all = await _chatService.GetAssistantProfilesAsync();
                saved = all.First(p => p.Id == _editingProfileId.Value);
            }
            else
            {
                saved = await _chatService.CreateAssistantProfileAsync(
                    Name.Trim(), BaseUrl.Trim(), ModelId.Trim(),
                    ApiKey.Trim(), Temperature, MaxTokens, IsDefault);
            }

            if (OnSaved != null)
                await OnSaved(saved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving assistant profile");
            ErrorMessage = $"Ошибка сохранения профиля: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        OnCancelled?.Invoke();
    }
}
