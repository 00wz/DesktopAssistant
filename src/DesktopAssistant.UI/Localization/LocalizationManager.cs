using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DesktopAssistant.UI.Localization;

/// <summary>
/// Manages the current UI language.
/// Loads a ResourceDictionary from Avalonia resources and adds it to
/// Application.Resources.MergedDictionaries, replacing the previous one.
/// Usage in XAML: <c>{DynamicResource KeyName}</c>.
/// </summary>
public sealed class LocalizationManager
{
    public static LocalizationManager Instance { get; } = new();

    public static IReadOnlyList<LanguageOption> AvailableLanguages { get; } = new LanguageOption[]
    {
        new("ru", "Русский"),
        new("en", "English"),
    };

    private IResourceDictionary? _currentDict;

    /// <summary>
    /// The language that will be loaded when <see cref="LoadLanguage"/> is called
    /// during application initialization.
    /// </summary>
    public string PendingLanguage { get; set; } = "en";

    public string CurrentLanguage { get; private set; } = "en";

    private LocalizationManager() { }

    /// <summary>
    /// Loads the dictionary for the specified language and applies it to the application resources.
    /// Must be called after <see cref="Application.Current"/> is available.
    /// </summary>
    public void LoadLanguage(string languageCode)
    {
        languageCode = Validate(languageCode);

        var uri = new Uri($"avares://DesktopAssistant.UI/Assets/Localization/Strings.{languageCode}.axaml");
        var newDict = (IResourceDictionary)AvaloniaXamlLoader.Load(uri);

        var merged = Avalonia.Application.Current!.Resources.MergedDictionaries;

        if (_currentDict != null)
            merged.Remove(_currentDict);

        merged.Insert(0, newDict);
        _currentDict = newDict;
        CurrentLanguage = languageCode;
    }

    private static string Validate(string code) =>
        AvailableLanguages.Any(l => l.Code == code) ? code : "ru";
}
