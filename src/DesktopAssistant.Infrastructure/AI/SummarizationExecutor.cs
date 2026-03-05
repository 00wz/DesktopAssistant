using System.Runtime.CompilerServices;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Application.Services;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Interfaces;
using DesktopAssistant.Infrastructure.AI.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI;

/// <summary>
/// Реализует суммаризацию контекста диалога через ChatHistorySummarizationReducer.
/// Использует профиль суммаризации из AppSettings, отдельный от профиля чата.
/// </summary>
public class SummarizationExecutor(
    ConversationService conversationService,
    IKernelFactory kernelFactory,
    ISecureCredentialStore credentialStore,
    IAppSettingsRepository appSettingsRepository,
    IAssistantProfileRepository assistantProfileRepository,
    ILogger<SummarizationExecutor> logger) : ISummarizationService
{
    private readonly ConversationService _conversationService = conversationService;
    private readonly IKernelFactory _kernelFactory = kernelFactory;
    private readonly ISecureCredentialStore _credentialStore = credentialStore;
    private readonly IAppSettingsRepository _appSettingsRepository = appSettingsRepository;
    private readonly IAssistantProfileRepository _assistantProfileRepository = assistantProfileRepository;
    private readonly ILogger<SummarizationExecutor> _logger = logger;

    /// <inheritdoc />
    public IAsyncEnumerable<SummarizationEvent> SummarizeAsync(
        Guid conversationId,
        Guid selectedNodeId,
        CancellationToken cancellationToken = default)
        => ExecuteCoreAsync(conversationId, selectedNodeId, cancellationToken);

    private async IAsyncEnumerable<SummarizationEvent> ExecuteCoreAsync(
        Guid conversationId,
        Guid selectedNodeId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 1. Resolve summarization profile
        var profileIdStr = await _appSettingsRepository.GetValueAsync(
            AppSettings.Keys.SummarizationProfileId, cancellationToken);

        if (!Guid.TryParse(profileIdStr, out var profileId))
            throw new InvalidOperationException(
                "Summarization profile is not configured. Please set it in application settings.");

        var profile = await _assistantProfileRepository.GetByIdAsync(profileId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Summarization profile {profileId} not found.");

        var apiKey = _credentialStore.GetApiKey(profileId)
            ?? throw new InvalidOperationException(
                $"API key is not set for summarization profile '{profile.Name}'.");

        // 2. Build context up to the selected node
        var contextMessages = await _conversationService.BuildContextAsync(selectedNodeId, cancellationToken);
        var chatHistory = contextMessages.ToChatHistory();

        // 3. Create kernel (plain, no agent tools) and reducer
        var kernel = _kernelFactory.Create(profile, apiKey);
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var reducer = new ChatHistorySummarizationReducer(chatCompletionService, targetCount: 1);

        yield return new SummarizationStartedDto();

        // 4. Run summarization
        _logger.LogInformation(
            "[SUMMARIZATION] Starting for node {SelectedNodeId} in conversation {ConversationId} ({MessageCount} messages)",
            selectedNodeId, conversationId, chatHistory.Count);

        var reducedMessages = (await reducer.ReduceAsync(chatHistory, cancellationToken))?.ToList()
            ?? throw new InvalidOperationException("Summarization returned no result.");

        _logger.LogInformation("[SUMMARIZATION] Completed. {Count} reduced messages.", reducedMessages.Count);

        // 5. Serialize reduced ChatMessageContent objects into metadata
        var serialized = reducedMessages
            .Select(m => Serialization.ChatMessageSerializer.Serialize(m))
            .ToArray();
        var metadata = new SummarizationMetadata(serialized).ToJson();

        // 6. Extract display text from reduced messages (role + content, blank line between)
        var summaryContent = string.Join("\n\n", reducedMessages
            .Where(m => !string.IsNullOrEmpty(m.Content))
            .Select(m => $"[{m.Role}]\n{m.Content}"));

        // 7. Inject summary node into the message tree
        var summaryNode = await _conversationService.InjectSummaryNodeAsync(
            conversationId, selectedNodeId, summaryContent, metadata, cancellationToken: cancellationToken);

        yield return new SummarizationCompletedDto(summaryNode.Id, summaryContent);
    }
}
