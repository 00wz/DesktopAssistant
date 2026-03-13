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
        var summarizationId = await GetSummarizationProfileIdAsync(cancellationToken);
        return profiles.Select(p => MapToDto(p, defaultId, summarizationId));
    }

    /// <inheritdoc />
    public async Task<AssistantProfileDto> CreateAssistantProfileAsync(
        string description,
        string baseUrl,
        string modelId,
        string apiKey,
        double temperature = 0.7,
        int maxTokens = 4096,
        CancellationToken cancellationToken = default)
    {
        var profile = new AssistantProfile(description, baseUrl, modelId,
            temperature: temperature, maxTokens: maxTokens);

        await _assistantRepository.AddAsync(profile, cancellationToken);
        _credentialStore.SetApiKey(profile.Id, apiKey);

        var existingDefaultId = await GetDefaultProfileIdAsync(cancellationToken);
        if (existingDefaultId is null)
            await StoreDefaultProfileIdAsync(profile.Id, cancellationToken);

        _logger.LogInformation("Created assistant profile {ProfileId}: {Description}", profile.Id, description);

        var defaultId = await GetDefaultProfileIdAsync(cancellationToken);
        var summarizationId = await GetSummarizationProfileIdAsync(cancellationToken);
        return MapToDto(profile, defaultId, summarizationId);
    }

    /// <inheritdoc />
    public async Task UpdateAssistantProfileAsync(
        Guid id,
        string description,
        string baseUrl,
        string modelId,
        double temperature,
        int maxTokens,
        CancellationToken cancellationToken = default)
    {
        var profile = await _assistantRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Assistant profile {id} not found");

        profile.UpdateDescription(description);
        profile.UpdateModelSettings(baseUrl, modelId, temperature, maxTokens);
        await _assistantRepository.UpdateAsync(profile, cancellationToken);

        _logger.LogInformation("Updated assistant profile {ProfileId}: {Description}", id, description);
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

        if (await GetDefaultProfileIdAsync(cancellationToken) == id)
            await _appSettingsRepository.SetAsync(AppSettings.Keys.DefaultProfileId, string.Empty, cancellationToken: cancellationToken);

        if (await GetSummarizationProfileIdAsync(cancellationToken) == id)
            await _appSettingsRepository.SetAsync(AppSettings.Keys.SummarizationProfileId, string.Empty, cancellationToken: cancellationToken);

        await _assistantRepository.DeleteAsync(id, cancellationToken);
        _logger.LogInformation("Deleted assistant profile {ProfileId}", id);
    }

    /// <inheritdoc />
    public async Task SetDefaultAssistantProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var profile = await _assistantRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Assistant profile {id} not found");

        await StoreDefaultProfileIdAsync(id, cancellationToken);
        _logger.LogInformation("Set default assistant profile {ProfileId}: {Description}", id, profile.Description);
    }

    /// <inheritdoc />
    public async Task SetSummarizationProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var profile = await _assistantRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Assistant profile {id} not found");

        await _appSettingsRepository.SetAsync(
            AppSettings.Keys.SummarizationProfileId,
            id.ToString(),
            "Summarization assistant profile ID",
            cancellationToken);
        _logger.LogInformation("Set summarization assistant profile {ProfileId}: {Description}", id, profile.Description);
    }

    private async Task<Guid?> GetDefaultProfileIdAsync(CancellationToken cancellationToken)
    {
        var value = await _appSettingsRepository.GetValueAsync(AppSettings.Keys.DefaultProfileId, cancellationToken);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private async Task<Guid?> GetSummarizationProfileIdAsync(CancellationToken cancellationToken)
    {
        var value = await _appSettingsRepository.GetValueAsync(AppSettings.Keys.SummarizationProfileId, cancellationToken);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private Task StoreDefaultProfileIdAsync(Guid profileId, CancellationToken cancellationToken) =>
        _appSettingsRepository.SetAsync(
            AppSettings.Keys.DefaultProfileId,
            profileId.ToString(),
            "Default assistant profile ID",
            cancellationToken);

    private AssistantProfileDto MapToDto(AssistantProfile p, Guid? defaultId, Guid? summarizationId) =>
        new(p.Id, p.Description, p.BaseUrl, p.ModelId, p.Temperature, p.MaxTokens, p.Id == defaultId,
            _credentialStore.HasApiKey(p.Id), p.Id == summarizationId);
}
