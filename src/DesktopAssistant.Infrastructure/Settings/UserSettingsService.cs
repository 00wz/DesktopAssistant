using System.Text.Json;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.Infrastructure.Settings;

/// <summary>
/// Stores the user's theme and language in %APPDATA%/DesktopAssistant/settings.json.
/// The file is created on the first write and updated atomically.
/// </summary>
public sealed class UserSettingsService : IUserSettingsService
{
    private readonly ILogger<UserSettingsService> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DesktopAssistant");

    public string SettingsFilePath { get; } = Path.Combine(SettingsDirectory, "settings.json");

    public UserSettingsService(ILogger<UserSettingsService> logger)
    {
        _logger = logger;
    }

    public async Task<UserSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            return await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SetThemeAsync(string theme, CancellationToken cancellationToken = default)
    {
        await UpdateAsync(settings => settings.Theme = theme, cancellationToken);
    }

    public async Task SetLanguageAsync(string language, CancellationToken cancellationToken = default)
    {
        await UpdateAsync(settings => settings.Language = language, cancellationToken);
    }

    private async Task UpdateAsync(Action<UserSettings> update, CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var settings = await LoadCoreAsync(cancellationToken) ?? new UserSettings();
            update(settings);
            await SaveCoreAsync(settings, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<UserSettings?> LoadCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return null;

            var json = await File.ReadAllTextAsync(SettingsFilePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<UserSettings>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load user settings from {Path}", SettingsFilePath);
            return null;
        }
    }

    private async Task SaveCoreAsync(UserSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);

            var tempPath = SettingsFilePath + ".tmp";
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);
            File.Move(tempPath, SettingsFilePath, overwrite: true);

            _logger.LogInformation("Saved user settings to {Path}", SettingsFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save user settings to {Path}", SettingsFilePath);
        }
    }
}
