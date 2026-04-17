using System.Runtime.CompilerServices;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Application.Services;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Infrastructure.AI.Extensions;
using DesktopAssistant.Infrastructure.AI.Kernel;
using DesktopAssistant.Infrastructure.AI.Metadata;
using DesktopAssistant.Infrastructure.AI.Plugins;
using DesktopAssistant.Infrastructure.AI.Serialization;
using DesktopAssistant.Infrastructure.AI.Streaming;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace DesktopAssistant.Infrastructure.AI.Executors;

/// <summary>
/// Executes a single LLM turn: builds context, streams the response, saves nodes, and creates pending tool nodes.
/// </summary>
public class LlmTurnExecutor(
    ConversationService conversationService,
    ISecureCredentialStore credentialStore,
    AgentKernelFactory agentKernelFactory,
    ILogger<LlmTurnExecutor> logger)
{
    private readonly ConversationService _conversationService = conversationService;
    private readonly ISecureCredentialStore _credentialStore = credentialStore;
    private readonly AgentKernelFactory _agentKernelFactory = agentKernelFactory;
    private readonly ILogger<LlmTurnExecutor> _logger = logger;

    /// <summary>
    /// Runs a single LLM turn starting from lastMessageId.
    /// Returns a stream of events: AssistantTurnDto → AssistantChunkDto* → AssistantResponseSavedDto
    /// → (ToolCallRequestedDto* if there are tool calls).
    /// </summary>
    public IAsyncEnumerable<StreamEvent> ExecuteAsync(
        Guid conversationId,
        Guid lastMessageId,
        CancellationToken cancellationToken = default)
    {
        return ExecuteCoreAsync(conversationId, lastMessageId, cancellationToken);
    }

    private async IAsyncEnumerable<StreamEvent> ExecuteCoreAsync(
        Guid conversationId,
        Guid lastMessageId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Load the conversation (for mode), system prompt and profile
        var conversation = await _conversationService.GetConversationAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found.");

        var systemPrompt = conversation.SystemPrompt;
        var profile = await _conversationService.GetAssistantProfileAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"AssistantProfile not found for conversation {conversationId}");

        var apiKey = _credentialStore.GetApiKey(profile.Id)
            ?? throw new InvalidOperationException(
                $"API key not found for profile '{profile.ModelId}' ({profile.Id}). Please set the API key in profile settings.");

        var contextMessages = await _conversationService.BuildContextAsync(lastMessageId, cancellationToken);
        var chatHistory = contextMessages.ToChatHistory(systemPrompt);

        var isAgentMode = conversation.Mode == ConversationMode.Agent;
        var kernel = _agentKernelFactory.Create(profile, apiKey, conversation.Mode, conversation.CanSpawnSubagents);
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = isAgentMode
                ? FunctionChoiceBehavior.Required(autoInvoke: false)
                : FunctionChoiceBehavior.Auto(autoInvoke: false)
        };

        yield return new AssistantTurnDto();

        var aggregator = new StreamingChatMessageAggregator();
        await foreach (var chunk in chatCompletionService.GetStreamingChatMessageContentsAsync(
            chatHistory, executionSettings, kernel, cancellationToken))
        {
            aggregator.Append(chunk);
            if (!string.IsNullOrEmpty(chunk.Content))
                yield return new AssistantChunkDto(chunk.Content);
        }

        var assistantMessage = aggregator.Build();
        var (inputTokenCount, outputTokenCount, totalTokenCount) = TokenUsageHelper.Extract(assistantMessage);

        var brokenCalls = FunctionCallContent.GetFunctionCalls(assistantMessage)
            .Where(c => c.Exception != null)
            .ToList();
        if (brokenCalls.Count > 0)
        {
            var details = string.Join("; ", brokenCalls.Select(c =>
                $"{c.PluginName}.{c.FunctionName}: {c.Exception!.Message}"));
            throw new InvalidOperationException(
                $"LLM returned {brokenCalls.Count} function call(s) with unparseable arguments. " +
                $"The assistant message will not be saved to preserve history integrity. Details: {details}");
        }

        var assistantMetadata = ChatMessageSerializer.Serialize(assistantMessage);
        var assistantNode = await _conversationService.AddNodeAsync(
            conversationId,
            lastMessageId,
            MessageNodeType.Assistant,
            assistantMessage.Content ?? string.Empty,
            assistantMetadata,
            cancellationToken: cancellationToken);

        _logger.LogInformation("[ASSISTANT MESSAGE] Saved {NodeId} ({Length} chars)",
            assistantNode.Id, assistantMessage.Content?.Length ?? 0);

        yield return new AssistantResponseSavedDto(assistantNode.Id, inputTokenCount, outputTokenCount, totalTokenCount);

        var functionCalls = FunctionCallContent.GetFunctionCalls(assistantMessage).ToList();
        if (functionCalls.Count == 0) yield break;

        _logger.LogInformation("[FUNCTION CALLS] LLM requested {Count} function calls", functionCalls.Count);

        var lastParentId = assistantNode.Id;
        foreach (var functionCall in functionCalls)
        {
            var callId = functionCall.Id ?? Guid.NewGuid().ToString();
            var argsJson = ToolNodeMetadata.SerializeFunctionArgs(functionCall);

            var pluginName = functionCall.PluginName ?? string.Empty;
            var isTerminal = pluginName == AgentOutputPlugin.PluginName;

            var toolMeta = new ToolNodeMetadata(
                callId,
                pluginName,
                functionCall.FunctionName,
                argsJson,
                ToolNodeStatus.Pending,
                IsTerminal: isTerminal);

            var pendingNode = await _conversationService.AddNodeAsync(
                conversationId,
                lastParentId,
                MessageNodeType.Tool,
                string.Empty,
                toolMeta.ToJson(),
                cancellationToken: cancellationToken);

            lastParentId = pendingNode.Id;

            _logger.LogDebug("[PENDING TOOL] Created node {NodeId} for {PluginName}.{FunctionName}",
                pendingNode.Id, toolMeta.PluginName, toolMeta.FunctionName);

            yield return new ToolCallRequestedDto(callId, toolMeta.PluginName, toolMeta.FunctionName, argsJson, pendingNode.Id, toolMeta.IsTerminal);
        }
    }

}
