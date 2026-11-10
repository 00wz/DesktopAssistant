using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.UI.Localization;
using DesktopAssistant.UI.Themes;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel for the "General" section of the settings panel.
/// Manages UI language and theme selection.
/// </summary>
public partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;
    private readonly IUserSettingsService _userSettingsService;

    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
        LocalizationManager.AvailableLanguages;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    public IReadOnlyList<ThemeOption> AvailableThemes { get; } =
        ThemeManager.AvailableThemes;

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    public GeneralSettingsViewModel(
        ILocalizationService localizationService,
        IUserSettingsService userSettingsService)
    {
        _localizationService = localizationService;
        _userSettingsService = userSettingsService;

        _selectedLanguage = AvailableLanguages
            .FirstOrDefault(l => l.Code == LocalizationManager.Instance.CurrentLanguage)
            ?? AvailableLanguages[0];

        _selectedTheme = AvailableThemes
            .FirstOrDefault(t => t.Code == ThemeManager.Instance.CurrentTheme)
            ?? AvailableThemes[0];
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is null) return;

        LocalizationManager.Instance.LoadLanguage(value.Code);
        _ = _localizationService.SetLanguageAsync(value.Code);
        _ = _userSettingsService.SetLanguageAsync(value.Code);
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value is null) return;

        ThemeManager.Instance.LoadTheme(value.Code);
        _ = _userSettingsService.SetThemeAsync(value.Code);
    }
}
