namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Отвечает за сохранение и загрузку выбранного языка интерфейса.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Возвращает сохранённый код языка или "ru", если настройка отсутствует.</summary>
    Task<string> GetSavedLanguageAsync();

    /// <summary>Сохраняет выбранный код языка в постоянном хранилище.</summary>
    Task SetLanguageAsync(string languageCode);
}
