using DesktopAssistant.Application.Dtos;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Сервис для управления профилями ассистента (CRUD, API-ключи, профиль по умолчанию).
/// </summary>
public interface IAssistantProfileService
{
    /// <summary>Возвращает все профили ассистента.</summary>
    Task<IEnumerable<AssistantProfileDto>> GetAssistantProfilesAsync(CancellationToken cancellationToken = default);

    /// <summary>Создаёт новый профиль ассистента. API-ключ сохраняется в защищённом хранилище. Если дефолтный профиль ещё не задан, созданный профиль становится дефолтным.</summary>
    Task<AssistantProfileDto> CreateAssistantProfileAsync(
        string name,
        string baseUrl,
        string modelId,
        string apiKey,
        double temperature = 0.7,
        int maxTokens = 4096,
        CancellationToken cancellationToken = default);

    /// <summary>Обновляет настройки профиля (без API-ключа).</summary>
    Task UpdateAssistantProfileAsync(
        Guid id,
        string name,
        string baseUrl,
        string modelId,
        double temperature,
        int maxTokens,
        CancellationToken cancellationToken = default);

    /// <summary>Обновляет API-ключ профиля в защищённом хранилище.</summary>
    Task SetAssistantProfileApiKeyAsync(Guid id, string apiKey, CancellationToken cancellationToken = default);

    /// <summary>Удаляет профиль ассистента.</summary>
    Task DeleteAssistantProfileAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Устанавливает указанный профиль как профиль по умолчанию, снимая флаг с предыдущего.</summary>
    Task SetDefaultAssistantProfileAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Устанавливает указанный профиль как профиль для суммаризации контекста.</summary>
    Task SetSummarizationProfileAsync(Guid id, CancellationToken cancellationToken = default);
}
