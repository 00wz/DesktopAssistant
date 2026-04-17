using System.Runtime.CompilerServices;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Application.Services;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Interfaces;
using DesktopAssistant.Infrastructure.AI.Extensions;
using DesktopAssistant.Infrastructure.AI.Metadata;
using DesktopAssistant.Infrastructure.AI.Summarization;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Executors;

/// <summary>
/// Implements conversation context summarization via <see cref="IChatHistoryReducer"/>.
/// Uses the summarization profile from AppSettings, separate from the chat profile.
/// The concrete reducer strategy is determined by <see cref="ISummarizationSchemaService"/>.
/// </summary>
public class SummarizationExecutor(
    ConversationService conversationService,
    IKernelFactory kernelFactory,
    ISecureCredentialStore credentialStore,
    IAppSettingsRepository appSettingsRepository,
    IAssistantProfileRepository assistantProfileRepository,
    ISummarizationSchemaService schemaService,
    IChatHistoryReducerFactory reducerFactory,
    ILogger<SummarizationExecutor> logger) : ISummarizationService
{
    private readonly ConversationService _conversationService = conversationService;
    private readonly IKernelFactory _kernelFactory = kernelFactory;
    private readonly ISecureCredentialStore _credentialStore = credentialStore;
    private readonly IAppSettingsRepository _appSettingsRepository = appSettingsRepository;
    private readonly IAssistantProfileRepository _assistantProfileRepository = assistantProfileRepository;
    private readonly ISummarizationSchemaService _schemaService = schemaService;
    private readonly IChatHistoryReducerFactory _reducerFactory = reducerFactory;
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
        // 1. Resolve summarization profile (falls back to default profile if not configured)
        var profile = await ResolveSummarizationProfileAsync(cancellationToken);

        var apiKey = _credentialStore.GetApiKey(profile.Id)
            ?? throw new InvalidOperationException(
                $"API key is not set for profile '{profile.ModelId}'.");

        // 2. Build context up to the selected node
        var contextMessages = await _conversationService.BuildContextAsync(selectedNodeId, cancellationToken);
        var chatHistory = contextMessages.ToChatHistory();

        // 3. Resolve schema and create the appropriate reducer
        var schema = await _schemaService.GetSchemaAsync(cancellationToken);
        var kernel = _kernelFactory.Create(profile, apiKey);
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var reducer = _reducerFactory.Create(chatCompletionService, schema);

        yield return new SummarizationStartedDto(selectedNodeId);

        // 4. Run summarization
        _logger.LogInformation(
            "[SUMMARIZATION] Starting for node {SelectedNodeId} in conversation {ConversationId} " +
            "({MessageCount} messages, schema={Schema})",
            selectedNodeId, conversationId, chatHistory.Count, schema);

        var reducedMessages = (await reducer.ReduceAsync(chatHistory, cancellationToken))?.ToList()
            ?? throw new InvalidOperationException("Summarization returned no result.");

        _logger.LogInformation("[SUMMARIZATION] Completed. {Count} reduced messages.", reducedMessages.Count);

        // 5. Serialize reduced ChatMessageContent objects into metadata
        var serialized = reducedMessages
            .Select(m => Serialization.ChatMessageSerializer.Serialize(m))
            .ToArray();
        var metadata = new SummarizationMetadata(serialized).ToJson();

        // 6. Extract display text from reduced messages (role + content/tool info, blank line between)
        var summaryContent = string.Join("\n\n", reducedMessages.Select(FormatMessageForDisplay));

        // 7. Inject summary node into the message tree
        var summaryNode = await _conversationService.InjectSummaryNodeAsync(
            conversationId, selectedNodeId, summaryContent, metadata, cancellationToken: cancellationToken);

        yield return new SummarizationCompletedDto(summaryNode.Id, selectedNodeId, summaryNode.CreatedAt, summaryContent);
    }

    private static string FormatMessageForDisplay(ChatMessageContent m)
    {
        var parts = new List<string>();

        foreach (var item in m.Items)
        {
            switch (item)
            {
                case TextContent tc when !string.IsNullOrEmpty(tc.Text):
                    parts.Add(tc.Text);
                    break;

                case FunctionCallContent fcc:
                    var argStr = fcc.Arguments is { Count: > 0 }
                        ? string.Join(", ", fcc.Arguments.Select(kv => $"{kv.Key}={kv.Value}"))
                        : string.Empty;
                    var callName = fcc.PluginName is not null
                        ? $"{fcc.PluginName}.{fcc.FunctionName}"
                        : fcc.FunctionName;
                    parts.Add($"[function_call] {callName}({argStr})");
                    break;

                case FunctionResultContent frc:
                    var resultName = frc.PluginName is not null
                        ? $"{frc.PluginName}.{frc.FunctionName}"
                        : frc.FunctionName;
                    var result = frc.Result is string s ? s : frc.Result?.ToString() ?? string.Empty;
                    parts.Add($"[function_result] {resultName} -> {result}");
                    break;
            }
        }

        var body = parts.Count > 0 ? "\n" + string.Join("\n", parts) : string.Empty;
        return $"[{m.Role}]{body}";
    }

    private async Task<AssistantProfile> ResolveSummarizationProfileAsync(
        CancellationToken cancellationToken)
    {
        var summarizationIdStr = await _appSettingsRepository.GetValueAsync(
            AppSettings.Keys.SummarizationProfileId, cancellationToken);

        if (Guid.TryParse(summarizationIdStr, out var summarizationId))
        {
            var profile = await _assistantProfileRepository.GetByIdAsync(summarizationId, cancellationToken);
            if (profile != null)
            {
                _logger.LogDebug("[SUMMARIZATION] Using summarization profile {ProfileId}", summarizationId);
                return profile;
            }
            _logger.LogWarning(
                "[SUMMARIZATION] Summarization profile {ProfileId} not found, falling back to default.", summarizationId);
        }

        var defaultIdStr = await _appSettingsRepository.GetValueAsync(
            AppSettings.Keys.DefaultProfileId, cancellationToken);

        if (!Guid.TryParse(defaultIdStr, out var defaultId))
            throw new InvalidOperationException(
                "No summarization profile configured and no default profile is set.");

        return await _assistantProfileRepository.GetByIdAsync(defaultId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Default profile {defaultId} not found.");
    }
}
