using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace DesktopAssistant.UI.Themes;

/// <summary>
/// Manages the current UI theme.
/// Loads a color palette ResourceDictionary from Avalonia resources and adds it to
/// Application.Resources.MergedDictionaries, replacing the previous one.
/// Usage in XAML: <c>{DynamicResource KeyName}</c>.
/// </summary>
public sealed class ThemeManager
{
    public static ThemeManager Instance { get; } = new();

    public static IReadOnlyList<ThemeOption> AvailableThemes { get; } = new ThemeOption[]
    {
        new("light", "Light"),
        new("dark", "Dark"),
        new("warm", "Warm"),
    };

    private IResourceDictionary? _currentDict;

    /// <summary>
    /// The theme that will be loaded when <see cref="LoadTheme"/> is called
    /// during application initialization.
    /// </summary>
    public string PendingTheme { get; set; } = "light";

    public string CurrentTheme { get; private set; } = "light";

    private ThemeManager() { }

    /// <summary>
    /// Loads the palette for the specified theme and applies it to the application resources.
    /// Must be called after <see cref="Avalonia.Application.Current"/> is available.
    /// </summary>
    public void LoadTheme(string themeCode)
    {
        themeCode = Validate(themeCode);

        var uri = new Uri($"avares://DesktopAssistant.UI/Assets/Themes/{themeCode}.axaml");
        var newDict = (IResourceDictionary)AvaloniaXamlLoader.Load(uri);

        var merged = Avalonia.Application.Current!.Resources.MergedDictionaries;

        if (_currentDict != null)
            merged.Remove(_currentDict);

        merged.Add(newDict);
        _currentDict = newDict;
        CurrentTheme = themeCode;

        // Keep FluentTheme controls (inputs, popups, scrollbars) in sync with the palette.
        Avalonia.Application.Current.RequestedThemeVariant =
            themeCode == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private static string Validate(string code) =>
        AvailableThemes.Any(t => t.Code == code) ? code : "light";
}
