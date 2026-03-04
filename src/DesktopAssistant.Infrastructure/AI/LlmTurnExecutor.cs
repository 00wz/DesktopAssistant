using System.Runtime.CompilerServices;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Application.Services;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Infrastructure.AI.Aggregation;
using DesktopAssistant.Infrastructure.AI.Extensions;
using DesktopAssistant.Infrastructure.AI.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace DesktopAssistant.Infrastructure.AI;

/// <summary>
/// Выполняет один LLM-тёрн: собирает контекст, стримит ответ, сохраняет узлы, создаёт pending tool-узлы.
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
    /// Запускает один LLM-тёрн начиная с lastMessageId.
    /// Возвращает поток событий: AssistantTurnDto → AssistantChunkDto* → AssistantResponseSavedDto
    /// → (ToolCallRequestedDto* если есть tool-вызовы).
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
        // Загружаем системный промпт и профиль из диалога
        var systemPrompt = await _conversationService.GetSystemPromptAsync(conversationId, cancellationToken);
        var profile = await _conversationService.GetAssistantProfileAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"AssistantProfile not found for conversation {conversationId}");

        var apiKey = _credentialStore.GetApiKey(profile.Id)
            ?? throw new InvalidOperationException(
                $"API key not found for profile '{profile.Name}' ({profile.Id}). Please set the API key in profile settings.");

        var contextMessages = await _conversationService.BuildContextAsync(lastMessageId, cancellationToken);
        var chatHistory = contextMessages.ToChatHistory(systemPrompt);

        var kernel = _agentKernelFactory.Create(profile, apiKey);
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false)
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
        LogUsage(inputTokenCount, outputTokenCount, totalTokenCount);

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

            var toolMeta = new ToolNodeMetadata(
                callId,
                functionCall.PluginName ?? string.Empty,
                functionCall.FunctionName,
                argsJson);

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

            yield return new ToolCallRequestedDto(callId, toolMeta.PluginName, toolMeta.FunctionName, argsJson, pendingNode.Id);
        }
    }

    private static void LogUsage(int inputTokenCount, int outputTokenCount, int totalTokenCount)
    {
        Console.WriteLine($"Input tokens: {inputTokenCount}");
        Console.WriteLine($"Output tokens: {outputTokenCount}");
        Console.WriteLine($"Total tokens: {totalTokenCount}");
    }

}
