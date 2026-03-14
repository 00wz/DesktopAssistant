using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.UI.Localization;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel секции «Общие» панели настроек.
/// Управляет выбором языка интерфейса.
/// </summary>
public partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;

    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
        LocalizationManager.AvailableLanguages;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    public GeneralSettingsViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService;

        _selectedLanguage = AvailableLanguages
            .FirstOrDefault(l => l.Code == LocalizationManager.Instance.CurrentLanguage)
            ?? AvailableLanguages[0];
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is null) return;

        LocalizationManager.Instance.LoadLanguage(value.Code);
        _ = _localizationService.SetLanguageAsync(value.Code);
    }
}
