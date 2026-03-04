using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.Infrastructure.AI;

/// <summary>
/// Реализация IAssistantProfileService — управление профилями ассистента.
/// </summary>
public class AssistantProfileService : IAssistantProfileService
{
    private readonly IAssistantProfileRepository _assistantRepository;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly ILogger<AssistantProfileService> _logger;

    public AssistantProfileService(
        IAssistantProfileRepository assistantRepository,
        ISecureCredentialStore credentialStore,
        ILogger<AssistantProfileService> logger)
    {
        _assistantRepository = assistantRepository;
        _credentialStore = credentialStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<AssistantProfileDto>> GetAssistantProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        var profiles = await _assistantRepository.GetAllAsync(cancellationToken);
        return profiles.Select(MapToDto);
    }

    /// <inheritdoc />
    public async Task<AssistantProfileDto> CreateAssistantProfileAsync(
        string name,
        string baseUrl,
        string modelId,
        string apiKey,
        double temperature = 0.7,
        int maxTokens = 4096,
        bool isDefault = false,
        CancellationToken cancellationToken = default)
    {
        if (isDefault)
        {
            var currentDefault = await _assistantRepository.GetDefaultAsync(cancellationToken);
            if (currentDefault != null)
            {
                currentDefault.UnsetDefault();
                await _assistantRepository.UpdateAsync(currentDefault, cancellationToken);
            }
        }

        var profile = new AssistantProfile(name, baseUrl, modelId,
            temperature: temperature, maxTokens: maxTokens, isDefault: isDefault);

        await _assistantRepository.AddAsync(profile, cancellationToken);
        _credentialStore.SetApiKey(profile.Id, apiKey);

        _logger.LogInformation("Created assistant profile {ProfileId}: {Name}", profile.Id, name);

        return MapToDto(profile);
    }

    /// <inheritdoc />
    public async Task UpdateAssistantProfileAsync(
        Guid id,
        string name,
        string baseUrl,
        string modelId,
        double temperature,
        int maxTokens,
        CancellationToken cancellationToken = default)
    {
        var profile = await _assistantRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Assistant profile {id} not found");

        profile.UpdateName(name);
        profile.UpdateModelSettings(baseUrl, modelId, temperature, maxTokens);
        await _assistantRepository.UpdateAsync(profile, cancellationToken);

        _logger.LogInformation("Updated assistant profile {ProfileId}: {Name}", id, name);
    }

    /// <inheritdoc />
    public Task SetAssistantProfileApiKeyAsync(Guid id, string apiKey, CancellationToken cancellationToken = default)
    {
        _credentialStore.SetApiKey(id, apiKey);
        _logger.LogInformation("Updated API key for profile {ProfileId}", id);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DeleteAssistantProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = await _assistantRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Assistant profile {id} not found");

        await _assistantRepository.DeleteAsync(id, cancellationToken);
        _logger.LogInformation("Deleted assistant profile {ProfileId}", id);
    }

    /// <inheritdoc />
    public async Task SetDefaultAssistantProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var profile = await _assistantRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Assistant profile {id} not found");

        var currentDefault = await _assistantRepository.GetDefaultAsync(cancellationToken);
        if (currentDefault != null && currentDefault.Id != id)
        {
            currentDefault.UnsetDefault();
            await _assistantRepository.UpdateAsync(currentDefault, cancellationToken);
        }

        profile.SetAsDefault();
        await _assistantRepository.UpdateAsync(profile, cancellationToken);
        _logger.LogInformation("Set default assistant profile {ProfileId}: {Name}", id, profile.Name);
    }

    private AssistantProfileDto MapToDto(AssistantProfile p) =>
        new(p.Id, p.Name, p.BaseUrl, p.ModelId, p.Temperature, p.MaxTokens, p.IsDefault,
            _credentialStore.HasApiKey(p.Id));
}
