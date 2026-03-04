using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.Domain.Entities;

/// <summary>
/// Глобальные настройки приложения
/// </summary>
public class AppSettings : BaseEntity
{
    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private AppSettings() { } // Для EF Core

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

    // Константы для ключей настроек
    public static class Keys
    {
        public const string DefaultProfileId = "App:DefaultProfileId";
    }
}
