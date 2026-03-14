using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopAssistant.Infrastructure.Settings;

/// <summary>
/// Хранит выбранный язык интерфейса в таблице AppSettings.
/// Ключ: <see cref="AppSettings.Keys.Language"/>.
/// По умолчанию возвращает "en".
/// </summary>
public class LocalizationService : ILocalizationService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public LocalizationService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<string> GetSavedLanguageAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAppSettingsRepository>();
        return await repo.GetValueAsync(AppSettings.Keys.Language) ?? "en";
    }

    public async Task SetLanguageAsync(string languageCode)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAppSettingsRepository>();
        await repo.SetAsync(AppSettings.Keys.Language, languageCode, "UI language");
    }
}
