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
    private readonly IAssistantProfileService _profileService;
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
    private string _description = string.Empty;

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
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Подсказка для поля API Key: зависит от режима.</summary>
    public string ApiKeyWatermark => IsEditMode
        ? "Оставьте пустым, чтобы сохранить текущий ключ"
        : "sk-...";

    public ProfileEditorViewModel(IAssistantProfileService profileService, ILogger<ProfileEditorViewModel> logger)
    {
        _profileService = profileService;
        _logger = logger;
    }

    /// <summary>Сбрасывает форму для создания нового профиля.</summary>
    public void InitCreate()
    {
        _editingProfileId = null;
        IsEditMode = false;
        Description = string.Empty;
        BaseUrl = string.Empty;
        ModelId = string.Empty;
        ApiKey = string.Empty;
        Temperature = 0.7;
        MaxTokens = 4096;
        ErrorMessage = null;
        IsLoading = false;
    }

    /// <summary>Заполняет форму данными существующего профиля для редактирования.</summary>
    public void InitEdit(AssistantProfileDto profile)
    {
        _editingProfileId = profile.Id;
        IsEditMode = true;
        Description = profile.Description;
        BaseUrl = profile.BaseUrl;
        ModelId = profile.ModelId;
        ApiKey = string.Empty;
        Temperature = profile.Temperature;
        MaxTokens = profile.MaxTokens;
        ErrorMessage = null;
        IsLoading = false;
    }

    private bool CanSave() =>
        !IsLoading &&
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
                await _profileService.UpdateAssistantProfileAsync(
                    _editingProfileId.Value,
                    Description.Trim(), BaseUrl.Trim(), ModelId.Trim(),
                    Temperature, MaxTokens);

                if (!string.IsNullOrWhiteSpace(ApiKey))
                    await _profileService.SetAssistantProfileApiKeyAsync(_editingProfileId.Value, ApiKey.Trim());

                var all = await _profileService.GetAssistantProfilesAsync();
                saved = all.First(p => p.Id == _editingProfileId.Value);
            }
            else
            {
                saved = await _profileService.CreateAssistantProfileAsync(
                    Description.Trim(), BaseUrl.Trim(), ModelId.Trim(),
                    ApiKey.Trim(), Temperature, MaxTokens);
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
