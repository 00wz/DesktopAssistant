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
    private readonly IAppSettingsRepository _appSettingsRepository;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly ILogger<AssistantProfileService> _logger;

    public AssistantProfileService(
        IAssistantProfileRepository assistantRepository,
        IAppSettingsRepository appSettingsRepository,
        ISecureCredentialStore credentialStore,
        ILogger<AssistantProfileService> logger)
    {
        _assistantRepository = assistantRepository;
        _appSettingsRepository = appSettingsRepository;
        _credentialStore = credentialStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<AssistantProfileDto>> GetAssistantProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        var profiles = await _assistantRepository.GetAllAsync(cancellationToken);
        var defaultId = await GetDefaultProfileIdAsync(cancellationToken);
        return profiles.Select(p => MapToDto(p, defaultId));
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
        var profile = new AssistantProfile(name, baseUrl, modelId,
            temperature: temperature, maxTokens: maxTokens);

        await _assistantRepository.AddAsync(profile, cancellationToken);
        _credentialStore.SetApiKey(profile.Id, apiKey);

        if (isDefault)
            await StoreDefaultProfileIdAsync(profile.Id, cancellationToken);

        _logger.LogInformation("Created assistant profile {ProfileId}: {Name}", profile.Id, name);

        var defaultId = await GetDefaultProfileIdAsync(cancellationToken);
        return MapToDto(profile, defaultId);
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

        await StoreDefaultProfileIdAsync(id, cancellationToken);
        _logger.LogInformation("Set default assistant profile {ProfileId}: {Name}", id, profile.Name);
    }

    private async Task<Guid?> GetDefaultProfileIdAsync(CancellationToken cancellationToken)
    {
        var value = await _appSettingsRepository.GetValueAsync(AppSettings.Keys.DefaultProfileId, cancellationToken);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private Task StoreDefaultProfileIdAsync(Guid profileId, CancellationToken cancellationToken) =>
        _appSettingsRepository.SetAsync(
            AppSettings.Keys.DefaultProfileId,
            profileId.ToString(),
            "Default assistant profile ID",
            cancellationToken);

    private AssistantProfileDto MapToDto(AssistantProfile p, Guid? defaultId) =>
        new(p.Id, p.Name, p.BaseUrl, p.ModelId, p.Temperature, p.MaxTokens, p.Id == defaultId,
            _credentialStore.HasApiKey(p.Id));
}
