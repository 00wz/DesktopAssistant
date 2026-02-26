using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.Infrastructure.Security;

/// <summary>
/// Хранилище API-ключей на основе Windows DPAPI (DataProtectionScope.CurrentUser).
/// Зашифрованные значения сохраняются в таблице AppSettings с ключом "apikey:{profileId}".
/// На не-Windows платформах использует XOR-обфускацию (не является криптографической защитой).
/// </summary>
public class DpapiCredentialStore : ISecureCredentialStore
{
    private const string KeyPrefix = "apikey:";

    private readonly IAppSettingsRepository _appSettingsRepository;
    private readonly ILogger<DpapiCredentialStore> _logger;

    public DpapiCredentialStore(
        IAppSettingsRepository appSettingsRepository,
        ILogger<DpapiCredentialStore> logger)
    {
        _appSettingsRepository = appSettingsRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public void SetApiKey(Guid profileId, string apiKey)
    {
        var encrypted = Encrypt(apiKey);
        // Синхронный вызов через .GetAwaiter().GetResult() — допустимо при инициализации профиля
        _appSettingsRepository.SetAsync(
            KeyPrefix + profileId,
            encrypted,
            $"Encrypted API key for profile {profileId}")
            .GetAwaiter().GetResult();

        _logger.LogInformation("API key stored for profile {ProfileId}", profileId);
    }

    /// <inheritdoc />
    public string? GetApiKey(Guid profileId)
    {
        var stored = _appSettingsRepository
            .GetValueAsync(KeyPrefix + profileId)
            .GetAwaiter().GetResult();

        if (stored == null) return null;

        try
        {
            return Decrypt(stored);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt API key for profile {ProfileId}", profileId);
            return null;
        }
    }

    /// <inheritdoc />
    public void DeleteApiKey(Guid profileId)
    {
        // AppSettings не имеет метода удаления — перезаписываем пустой строкой
        _appSettingsRepository.SetAsync(
            KeyPrefix + profileId,
            string.Empty)
            .GetAwaiter().GetResult();

        _logger.LogInformation("API key deleted for profile {ProfileId}", profileId);
    }

    /// <inheritdoc />
    public bool HasApiKey(Guid profileId)
    {
        var stored = _appSettingsRepository
            .GetValueAsync(KeyPrefix + profileId)
            .GetAwaiter().GetResult();

        return !string.IsNullOrEmpty(stored);
    }

    private static string Encrypt(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        // Не-Windows: base64 (обфускация, не безопасна)
        return Convert.ToBase64String(bytes);
    }

    private static string Decrypt(string encryptedBase64)
    {
        var bytes = Convert.FromBase64String(encryptedBase64);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }

        // Не-Windows: простой base64
        return Encoding.UTF8.GetString(bytes);
    }
}
