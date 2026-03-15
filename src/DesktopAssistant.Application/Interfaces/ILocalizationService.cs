namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Responsible for saving and loading the selected UI language.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Returns the saved language code, or "en" if no setting exists.</summary>
    Task<string> GetSavedLanguageAsync();

    /// <summary>Saves the selected language code to persistent storage.</summary>
    Task SetLanguageAsync(string languageCode);
}
