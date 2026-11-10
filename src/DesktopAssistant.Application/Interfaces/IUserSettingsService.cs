using DesktopAssistant.Application.Dtos;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Loads and saves user interface preferences (theme and language)
/// in the local settings.json file.
/// </summary>
public interface IUserSettingsService
{
    /// <summary>Loads the saved settings, or null when no settings file exists.</summary>
    Task<UserSettings?> LoadAsync(CancellationToken cancellationToken = default);

    Task SetThemeAsync(string theme, CancellationToken cancellationToken = default);

    Task SetLanguageAsync(string language, CancellationToken cancellationToken = default);
}
