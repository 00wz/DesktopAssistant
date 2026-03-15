using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.Domain.Entities;

/// <summary>
/// Global application settings.
/// </summary>
public class AppSettings : BaseEntity
{
    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private AppSettings() { } // For EF Core

    public AppSettings(string key, string value, string? description = null)
    {
        Key = key;
        Value = value;
        Description = description;
    }

    public void UpdateValue(string value)
    {
        Value = value;
        MarkAsUpdated();
    }

    // Constants for settings keys
    public static class Keys
    {
        public const string DefaultProfileId = "App:DefaultProfileId";
        public const string SummarizationProfileId = "App:SummarizationProfileId";
        public const string Language = "App:Language";
    }
}
