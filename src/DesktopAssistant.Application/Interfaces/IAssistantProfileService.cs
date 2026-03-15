using DesktopAssistant.Application.Dtos;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Service for managing assistant profiles (CRUD, API keys, default profile).
/// </summary>
public interface IAssistantProfileService
{
    /// <summary>Returns all assistant profiles.</summary>
    Task<IEnumerable<AssistantProfileDto>> GetAssistantProfilesAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a new assistant profile. The API key is saved in the secure store. If no default profile is set yet, the created profile becomes the default.</summary>
    Task<AssistantProfileDto> CreateAssistantProfileAsync(
        string description,
        string baseUrl,
        string modelId,
        string apiKey,
        double temperature = 0.7,
        int maxTokens = 4096,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the profile settings (without the API key).</summary>
    Task UpdateAssistantProfileAsync(
        Guid id,
        string description,
        string baseUrl,
        string modelId,
        double temperature,
        int maxTokens,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the profile's API key in the secure store.</summary>
    Task SetAssistantProfileApiKeyAsync(Guid id, string apiKey, CancellationToken cancellationToken = default);

    /// <summary>Deletes an assistant profile.</summary>
    Task DeleteAssistantProfileAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Sets the specified profile as the default, clearing the flag from the previous default.</summary>
    Task SetDefaultAssistantProfileAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Sets the specified profile as the summarization context profile.</summary>
    Task SetSummarizationProfileAsync(Guid id, CancellationToken cancellationToken = default);
}
