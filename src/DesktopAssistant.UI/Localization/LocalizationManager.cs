using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DesktopAssistant.UI.Localization;

/// <summary>
/// Управляет текущим языком интерфейса.
/// Загружает ResourceDictionary из Avalonia-ресурсов и добавляет его в
/// Application.Resources.MergedDictionaries, заменяя предыдущий.
/// Использование в XAML: <c>{DynamicResource KeyName}</c>.
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
    /// Язык, который будет загружен при вызове <see cref="LoadLanguage"/>
    /// во время инициализации приложения.
    /// </summary>
    public string PendingLanguage { get; set; } = "en";

    public string CurrentLanguage { get; private set; } = "en";

    private LocalizationManager() { }

    /// <summary>
    /// Загружает словарь для указанного языка и применяет его к ресурсам приложения.
    /// Должен вызываться после того, как <see cref="Application.Current"/> доступен.
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
