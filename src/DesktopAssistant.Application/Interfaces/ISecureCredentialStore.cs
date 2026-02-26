namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Защищённое хранилище API-ключей для профилей ассистентов.
/// На Windows использует DPAPI (DataProtectionScope.CurrentUser).
/// </summary>
public interface ISecureCredentialStore
{
    /// <summary>Сохраняет API-ключ для указанного профиля.</summary>
    void SetApiKey(Guid profileId, string apiKey);

    /// <summary>Возвращает API-ключ для указанного профиля, или null если ключ не найден.</summary>
    string? GetApiKey(Guid profileId);

    /// <summary>Удаляет API-ключ для указанного профиля.</summary>
    void DeleteApiKey(Guid profileId);

    /// <summary>Проверяет наличие сохранённого API-ключа для указанного профиля.</summary>
    bool HasApiKey(Guid profileId);
}
