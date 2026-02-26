using System.Runtime.CompilerServices;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Application.Services;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Infrastructure.AI.Aggregation;
using DesktopAssistant.Infrastructure.AI.Extensions;
using DesktopAssistant.Infrastructure.AI.Filters;
using DesktopAssistant.Infrastructure.AI.Serialization;
using DesktopAssistant.Infrastructure.MCP.Plugins;
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
    IMcpServerManager mcpServerManager,
    IMcpConfigurationService mcpConfigurationService,
    ILoggerFactory loggerFactory,
    ILogger<LlmTurnExecutor> logger)
{
    private readonly ConversationService _conversationService = conversationService;
    private readonly ISecureCredentialStore _credentialStore = credentialStore;
    private readonly IMcpServerManager _mcpServerManager = mcpServerManager;
    private readonly IMcpConfigurationService _mcpConfigurationService = mcpConfigurationService;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
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
        var chatHistory = BuildChatHistory(contextMessages, systemPrompt);

        var kernel = CreateKernelWithMcpTools(profile, apiKey);
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

        yield return new AssistantResponseSavedDto(assistantNode.Id);

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

    /// <summary>
    /// Строит ChatHistory из узлов диалога.
    /// Системный промпт инжектируется первым если не пустой.
    /// System-узлы из дерева (пустые якорные узлы) пропускаются.
    /// </summary>
    private static ChatHistory BuildChatHistory(IEnumerable<MessageNode> messages, string systemPrompt)
    {
        var chatHistory = new ChatHistory();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
            chatHistory.AddSystemMessage(systemPrompt);

        foreach (var message in messages)
        {
            if (message.NodeType == MessageNodeType.System)
            {
                // Пустые якорные System-узлы пропускаются
                continue;
            }
            else if (message.NodeType == MessageNodeType.Tool)
            {
                var toolChatMsg = GetToolChatMessageContent(message);
                if (toolChatMsg != null)
                    chatHistory.Add(toolChatMsg);
                // pending-узел (toolChatMsg == null) пропускается
            }
            else if (message.NodeType == MessageNodeType.Summary)
            {
                var chatMessage = message.GetOrCreateChatMessageContent();
                chatHistory.AddSystemMessage($"[Previous conversation summary]: {chatMessage.Content}");
            }
            else
            {
                chatHistory.Add(message.GetOrCreateChatMessageContent());
            }
        }

        return chatHistory;
    }

    /// <summary>
    /// Извлекает ChatMessageContent из tool-узла.
    /// Новый формат: берёт SerializedChatMessage из ToolNodeMetadata.
    /// Старый формат: десериализует Metadata напрямую.
    /// Возвращает null для pending-узлов (ResultJson == null).
    /// </summary>
    private static ChatMessageContent? GetToolChatMessageContent(MessageNode node)
    {
        var meta = ToolNodeMetadata.TryDeserialize(node.Metadata);

        if (meta != null)
        {
            if (meta.ResultJson == null) return null;
            if (string.IsNullOrEmpty(meta.SerializedChatMessage)) return null;
            return ChatMessageSerializer.TryDeserialize(meta.SerializedChatMessage, out var cm) ? cm : null;
        }

        return node.GetOrCreateChatMessageContent();
    }

    private Kernel CreateKernelWithMcpTools(AssistantProfile profile, string apiKey)
    {
        var kernel = KernelFactory.Create(profile, apiKey);

        kernel.FunctionInvocationFilters.Add(
            new FunctionLoggingFilter(_loggerFactory.CreateLogger<FunctionLoggingFilter>()));

        kernel.ImportPluginFromObject(
            new CoreToolsPlugin(_loggerFactory.CreateLogger<CoreToolsPlugin>()), "CoreTools");

        kernel.ImportPluginFromObject(
            new McpManagementPlugin(
                _loggerFactory.CreateLogger<McpManagementPlugin>(),
                _mcpServerManager,
                _mcpConfigurationService),
            "McpManagement");

        if (_mcpServerManager.GetConnectedServers().Count > 0)
        {
            var mcpToolsPlugin = new McpToolsPlugin(
                _mcpServerManager,
                _loggerFactory.CreateLogger<McpToolsPlugin>());
            mcpToolsPlugin.RegisterToolsToKernel(kernel);
        }

        return kernel;
    }
}
