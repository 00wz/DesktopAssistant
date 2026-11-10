namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// User preferences persisted in the local settings.json file.
/// </summary>
public sealed class UserSettings
{
    public string Theme { get; set; } = "light";

    public string Language { get; set; } = "en";
}
