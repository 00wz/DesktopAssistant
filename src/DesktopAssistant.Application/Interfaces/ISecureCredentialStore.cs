namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Secure storage for API keys associated with assistant profiles.
/// On Windows uses DPAPI (DataProtectionScope.CurrentUser).
/// </summary>
public interface ISecureCredentialStore
{
    /// <summary>Saves the API key for the specified profile.</summary>
    void SetApiKey(Guid profileId, string apiKey);

    /// <summary>Returns the API key for the specified profile, or null if no key is stored.</summary>
    string? GetApiKey(Guid profileId);

    /// <summary>Deletes the API key for the specified profile.</summary>
    void DeleteApiKey(Guid profileId);

    /// <summary>Checks whether an API key is stored for the specified profile.</summary>
    bool HasApiKey(Guid profileId);
}
