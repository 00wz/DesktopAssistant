using System.Runtime.CompilerServices;
using System.Text.Json;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Application.Services;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Domain.Interfaces;
using DesktopAssistant.Infrastructure.AI.Aggregation;
using DesktopAssistant.Infrastructure.AI.Filters;
using DesktopAssistant.Infrastructure.AI.Extensions;
using DesktopAssistant.Infrastructure.AI.Serialization;
using DesktopAssistant.Infrastructure.MCP.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace DesktopAssistant.Infrastructure.AI;

/// <summary>
/// Сервис для взаимодействия с LLM через Semantic Kernel
/// </summary>
public class ChatService : IChatService
{
    private readonly IKernelFactory _kernelFactory;
    private readonly LlmOptions _llmOptions;
    private readonly ConversationService _conversationService;
    private readonly IConversationRepository _conversationRepository;
    private readonly IMessageNodeRepository _messageNodeRepository;
    private readonly IAssistantProfileRepository _assistantRepository;
    private readonly IMcpServerManager _mcpServerManager;
    private readonly IMcpConfigurationService _mcpConfigurationService;
    private readonly ILogger<ChatService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Sentinel-значение в Content узла ожидающего tool-вызова.</summary>
    private const string PendingToolSentinel = "__PENDING_TOOL__";

    /// <summary>Метаданные ожидающего tool-узла, сериализуются в MessageNode.Metadata.</summary>
    private sealed record PendingToolCallMetadata(
        string CallId,
        string PluginName,
        string FunctionName,
        string ArgumentsJson);

    public ChatService(
        IKernelFactory kernelFactory,
        IOptions<LlmOptions> llmOptions,
        ConversationService conversationService,
        IConversationRepository conversationRepository,
        IMessageNodeRepository messageNodeRepository,
        IAssistantProfileRepository assistantRepository,
        IMcpServerManager mcpServerManager,
        IMcpConfigurationService mcpConfigurationService,
        ILoggerFactory loggerFactory,
        ILogger<ChatService> logger)
    {
        _kernelFactory = kernelFactory;
        _llmOptions = llmOptions.Value;
        _conversationService = conversationService;
        _conversationRepository = conversationRepository;
        _messageNodeRepository = messageNodeRepository;
        _assistantRepository = assistantRepository;
        _mcpServerManager = mcpServerManager;
        _mcpConfigurationService = mcpConfigurationService;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Conversation> CreateConversationAsync(
        string title,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        var assistant = await _assistantRepository.GetDefaultAsync(cancellationToken);

        if (assistant == null)
        {
            assistant = new AssistantProfile(
                name: "Default Assistant",
                systemPrompt: systemPrompt ?? "You are a helpful AI assistant.",
                baseUrl: _llmOptions.BaseUrl,
                modelId: _llmOptions.Model,
                isDefault: true);
            await _assistantRepository.AddAsync(assistant, cancellationToken);
            _logger.LogInformation("Created default assistant profile");
        }
        else if (!string.IsNullOrEmpty(systemPrompt))
        {
            assistant = new AssistantProfile(
                name: $"Assistant for {title}",
                systemPrompt: systemPrompt,
                baseUrl: _llmOptions.BaseUrl,
                modelId: _llmOptions.Model,
                isDefault: false);
            await _assistantRepository.AddAsync(assistant, cancellationToken);
        }

        var conversation = await _conversationService.CreateConversationAsync(
            title,
            assistant.Id,
            cancellationToken);

        _logger.LogInformation("Created conversation {ConversationId}: {Title}", conversation.Id, title);
        return conversation;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Conversation>> GetConversationsAsync(CancellationToken cancellationToken = default)
    {
        return await _conversationService.GetActiveConversationsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<MessageDto>> GetConversationHistoryAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken);
        if (conversation == null)
        {
            _logger.LogWarning("Conversation {ConversationId} not found", conversationId);
            return Enumerable.Empty<MessageDto>();
        }

        if (!conversation.ActiveLeafNodeId.HasValue)
        {
            _logger.LogWarning("Conversation {ConversationId} has no active leaf node", conversationId);
            return Enumerable.Empty<MessageDto>();
        }

        var nodes = (await _conversationService.GetBranchPathAsync(
            conversation.ActiveLeafNodeId.Value, cancellationToken)).ToList();

        // Системные узлы не отображаются в UI
        var visibleNodes = nodes.Where(n => n.NodeType != MessageNodeType.System).ToList();

        // Вычисляем sibling info: один запрос на уникальный parentId
        var parentIds = visibleNodes
            .Where(n => n.ParentId.HasValue)
            .Select(n => n.ParentId!.Value)
            .Distinct()
            .ToList();

        var siblingsMap = new Dictionary<Guid, List<MessageNode>>();
        foreach (var parentId in parentIds)
        {
            var siblings = (await _messageNodeRepository.GetChildrenAsync(parentId, cancellationToken)).ToList();
            siblingsMap[parentId] = siblings;
        }

        return visibleNodes.Select(n => MapNodeToDto(n, siblingsMap)).ToList();
    }

    /// <inheritdoc />
    public async Task SwitchToSiblingAsync(
        Guid conversationId,
        Guid parentNodeId,
        Guid newChildId,
        CancellationToken cancellationToken = default)
    {
        await _conversationService.SwitchToSiblingAsync(
            conversationId,
            parentNodeId,
            newChildId,
            cancellationToken);

        _logger.LogDebug("Switched to sibling {NewChildId} in conversation {ConversationId}",
            newChildId, conversationId);
    }

    /// <inheritdoc />
    public async Task<UserMessageDto> AddUserMessageAsync(
        Guid conversationId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        if (!conversation.ActiveLeafNodeId.HasValue)
            throw new InvalidOperationException($"Conversation {conversationId} has no active leaf node");

        return await AddUserMessageAsync(conversationId, conversation.ActiveLeafNodeId.Value, content, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UserMessageDto> AddUserMessageAsync(
        Guid conversationId,
        Guid parentNodeId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var userMessage = new ChatMessageContent(AuthorRole.User, content);
        var userNode = await _conversationService.AddChatMessageAsync(
            conversationId,
            parentNodeId,
            userMessage,
            cancellationToken: cancellationToken);

        _logger.LogInformation("[USER MESSAGE] {MessageId}:\n{Content}", userNode.Id, content);

        // Вычисляем sibling info для нового узла
        var siblings = (await _messageNodeRepository.GetChildrenAsync(parentNodeId, cancellationToken)).ToList();
        var userSiblings = siblings
            .Where(s => s.NodeType == MessageNodeType.User)
            .OrderBy(s => s.CreatedAt)
            .ToList();

        int total = userSiblings.Count;
        int idx = userSiblings.FindIndex(s => s.Id == userNode.Id);
        int currentIndex = idx >= 0 ? idx + 1 : 1;
        Guid? prevId = idx > 0 ? userSiblings[idx - 1].Id : null;
        Guid? nextId = idx < total - 1 ? userSiblings[idx + 1].Id : null;

        return new UserMessageDto(
            userNode.Id,
            userNode.ParentId,
            userNode.CreatedAt,
            userNode.Content,
            currentIndex,
            total,
            idx > 0,
            idx < total - 1,
            prevId,
            nextId);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<StreamEvent> GetAssistantResponseAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        return GetAssistantResponseCoreAsync(conversationId, lastMessageId: null, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<StreamEvent> GetAssistantResponseAsync(
        Guid conversationId,
        Guid lastMessageId,
        CancellationToken cancellationToken = default)
    {
        return GetAssistantResponseCoreAsync(conversationId, lastMessageId, cancellationToken);
    }

    private async IAsyncEnumerable<StreamEvent> GetAssistantResponseCoreAsync(
        Guid conversationId,
        Guid? lastMessageId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Определяем lastMessageId если не передан
        if (!lastMessageId.HasValue)
        {
            var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
                ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

            if (!conversation.ActiveLeafNodeId.HasValue)
                throw new InvalidOperationException($"Conversation {conversationId} has no active leaf node");

            lastMessageId = conversation.ActiveLeafNodeId.Value;
        }

        // Собираем контекст
        var contextMessages = await _conversationService.BuildContextAsync(lastMessageId.Value, cancellationToken);
        var chatHistory = BuildChatHistory(contextMessages);

        var kernel = CreateKernelWithMcpTools();
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false)
        };

        // Один LLM-тёрн: стримим ответ
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

        // Сохраняем сообщение ассистента в БД немедленно
        var assistantNode = await _conversationService.AddChatMessageAsync(
            conversationId,
            lastMessageId.Value,
            assistantMessage,
            cancellationToken: cancellationToken);

        _logger.LogInformation("[ASSISTANT MESSAGE] Saved {NodeId} ({Length} chars)",
            assistantNode.Id, assistantMessage.Content?.Length ?? 0);

        // Уведомляем UI об ID сохранённого узла ассистента
        yield return new AssistantResponseSavedDto(assistantNode.Id);

        var functionCalls = FunctionCallContent.GetFunctionCalls(assistantMessage).ToList();

        if (functionCalls.Count == 0)
        {
            // Path A: нет tool-вызовов — тёрн завершён
            yield break;
        }

        // Path B: есть tool-вызовы — создаём pending-узлы в БД и сигнализируем UI
        _logger.LogInformation("[FUNCTION CALLS] LLM requested {Count} function calls", functionCalls.Count);

        var lastParentId = assistantNode.Id;
        foreach (var functionCall in functionCalls)
        {
            var callId = functionCall.Id ?? Guid.NewGuid().ToString();
            var argsJson = SerializeFunctionArgs(functionCall);
            var pendingMeta = new PendingToolCallMetadata(
                callId,
                functionCall.PluginName ?? string.Empty,
                functionCall.FunctionName,
                argsJson);

            var pendingNode = await CreatePendingToolNodeAsync(
                conversationId, lastParentId, pendingMeta, cancellationToken);

            lastParentId = pendingNode.Id;

            _logger.LogDebug("[PENDING TOOL] Created node {NodeId} for {PluginName}.{FunctionName}",
                pendingNode.Id, pendingMeta.PluginName, pendingMeta.FunctionName);

            yield return new ToolCallRequestedDto(
                callId,
                pendingMeta.PluginName,
                pendingMeta.FunctionName,
                argsJson,
                pendingNode.Id);
        }
        // Итератор завершается — поток событий закрыт до следующего GetAssistantResponseAsync
    }

    /// <inheritdoc />
    public async Task<ToolCallResult> ApproveToolCallAsync(
        Guid pendingNodeId,
        CancellationToken cancellationToken = default)
    {
        var pendingNode = await _messageNodeRepository.GetByIdAsync(pendingNodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Pending tool node {pendingNodeId} not found");

        if (pendingNode.Content != PendingToolSentinel)
            throw new InvalidOperationException($"Node {pendingNodeId} is not a pending tool node (Content: {pendingNode.Content})");

        var meta = JsonSerializer.Deserialize<PendingToolCallMetadata>(pendingNode.Metadata!, _jsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse pending tool metadata for node {pendingNodeId}");

        var kernel = CreateKernelWithMcpTools();

        string resultJson;
        bool isError = false;
        string? errorMsg = null;

        try
        {
            _logger.LogDebug("[TOOL APPROVE] Invoking {PluginName}.{FunctionName}", meta.PluginName, meta.FunctionName);

            KernelArguments? kernelArgs = null;
            if (!string.IsNullOrEmpty(meta.ArgumentsJson) && meta.ArgumentsJson != "{}")
            {
                var argsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(meta.ArgumentsJson, _jsonOptions);
                if (argsDict != null)
                {
                    kernelArgs = [];
                    foreach (var kv in argsDict)
                        kernelArgs[kv.Key] = kv.Value;
                }
            }

            var result = await kernel.InvokeAsync(meta.PluginName, meta.FunctionName, kernelArgs, cancellationToken);
            resultJson = result.ToString() ?? string.Empty;

            _logger.LogDebug("[TOOL RESULT] {PluginName}.{FunctionName} → {Result}",
                meta.PluginName, meta.FunctionName, resultJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TOOL ERROR] {PluginName}.{FunctionName} failed", meta.PluginName, meta.FunctionName);
            resultJson = ex.Message;
            isError = true;
            errorMsg = ex.Message;
        }

        // Обновляем узел реальным результатом
        var functionCallForResult = new FunctionCallContent(meta.FunctionName, meta.PluginName, meta.CallId);
        var resultContent = new FunctionResultContent(functionCallForResult, resultJson);
        var resultChatMsg = resultContent.ToChatMessage();

        pendingNode.UpdateContent(resultJson);
        pendingNode.SetMetadata(ChatMessageSerializer.Serialize(resultChatMsg));
        await _messageNodeRepository.UpdateAsync(pendingNode, cancellationToken);

        bool allComplete = await CheckAllToolsCompleteAsync(pendingNode.ConversationId, cancellationToken);

        return new ToolCallResult(isError, resultJson, errorMsg, allComplete);
    }

    /// <inheritdoc />
    public async Task<ToolCallResult> DenyToolCallAsync(
        Guid pendingNodeId,
        CancellationToken cancellationToken = default)
    {
        var pendingNode = await _messageNodeRepository.GetByIdAsync(pendingNodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Pending tool node {pendingNodeId} not found");

        var meta = JsonSerializer.Deserialize<PendingToolCallMetadata>(pendingNode.Metadata ?? "{}", _jsonOptions);

        const string deniedResult = "Denied by user";

        // Обновляем узел статусом отклонения
        var functionCallForResult = new FunctionCallContent(
            meta?.FunctionName ?? string.Empty,
            meta?.PluginName ?? string.Empty,
            meta?.CallId ?? string.Empty);
        var deniedContent = new FunctionResultContent(functionCallForResult, deniedResult);
        var deniedChatMsg = deniedContent.ToChatMessage();

        pendingNode.UpdateContent(deniedResult);
        pendingNode.SetMetadata(ChatMessageSerializer.Serialize(deniedChatMsg));
        await _messageNodeRepository.UpdateAsync(pendingNode, cancellationToken);

        _logger.LogInformation("[TOOL DENIED] Node {NodeId}: {PluginName}.{FunctionName}",
            pendingNodeId, meta?.PluginName, meta?.FunctionName);

        bool allComplete = await CheckAllToolsCompleteAsync(pendingNode.ConversationId, cancellationToken);

        return new ToolCallResult(false, deniedResult, null, allComplete);
    }

    /// <summary>
    /// Проверяет, все ли tool-узлы текущего тёрна выполнены (нет ни одного с Content == PendingToolSentinel).
    /// Проходит назад от ActiveLeafNodeId, собирая Tool-узлы до первого не-Tool узла.
    /// </summary>
    private async Task<bool> CheckAllToolsCompleteAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken);
        if (conversation?.ActiveLeafNodeId == null)
            return true;

        await foreach (var node in _messageNodeRepository.TraverseToRootAsync(
            conversation.ActiveLeafNodeId.Value, cancellationToken))
        {
            if (node.NodeType != MessageNodeType.Tool)
                break;

            if (node.Content == PendingToolSentinel)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Создаёт pending tool-узел в БД: Content = PendingToolSentinel, Metadata = PendingToolCallMetadata JSON.
    /// Обновляет ActiveChildId родителя и ActiveLeafNodeId диалога.
    /// </summary>
    private async Task<MessageNode> CreatePendingToolNodeAsync(
        Guid conversationId,
        Guid parentNodeId,
        PendingToolCallMetadata pendingMeta,
        CancellationToken cancellationToken)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        var parentNode = await _messageNodeRepository.GetByIdAsync(parentNodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Parent node {parentNodeId} not found");

        var metaJson = JsonSerializer.Serialize(pendingMeta, _jsonOptions);
        var node = new MessageNode(conversationId, MessageNodeType.Tool, PendingToolSentinel, parentNodeId);
        node.SetMetadata(metaJson);

        await _messageNodeRepository.AddAsync(node, cancellationToken);

        parentNode.SetActiveChild(node.Id);
        await _messageNodeRepository.UpdateAsync(parentNode, cancellationToken);

        conversation.SetActiveLeafNode(node.Id);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);

        return node;
    }

    private static ChatHistory BuildChatHistory(IEnumerable<MessageNode> messages)
    {
        var chatHistory = new ChatHistory();

        foreach (var message in messages)
        {
            var chatMessage = message.GetOrCreateChatMessageContent();

            if (message.NodeType == MessageNodeType.Summary)
            {
                chatHistory.AddSystemMessage($"[Previous conversation summary]: {chatMessage.Content}");
            }
            else
            {
                chatHistory.Add(chatMessage);
            }
        }

        return chatHistory;
    }

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    private static string SerializeFunctionArgs(FunctionCallContent functionCall)
    {
        if (functionCall.Arguments == null) return "{}";
        try
        {
            return JsonSerializer.Serialize(functionCall.Arguments, _jsonOptions);
        }
        catch
        {
            return functionCall.Arguments.ToString() ?? "{}";
        }
    }

    private MessageDto MapNodeToDto(MessageNode node, Dictionary<Guid, List<MessageNode>> siblingsMap)
    {
        var (currentIndex, total, hasPrev, hasNext, prevId, nextId) = ComputeSiblingInfo(node, siblingsMap);

        return node.NodeType switch
        {
            MessageNodeType.User => new UserMessageDto(
                node.Id, node.ParentId, node.CreatedAt, node.Content,
                currentIndex, total, hasPrev, hasNext, prevId, nextId),

            MessageNodeType.Assistant => new AssistantMessageDto(
                node.Id, node.ParentId, node.CreatedAt, node.Content,
                currentIndex, total, hasPrev, hasNext, prevId, nextId),

            MessageNodeType.Tool => MapToolNodeToDto(node),

            MessageNodeType.Summary => new SummaryMessageDto(
                node.Id, node.ParentId, node.CreatedAt, node.Content, 0, 0),

            _ => new AssistantMessageDto(node.Id, node.ParentId, node.CreatedAt, node.Content)
        };
    }

    private static (int currentIndex, int total, bool hasPrev, bool hasNext, Guid? prevId, Guid? nextId) ComputeSiblingInfo(
        MessageNode node, Dictionary<Guid, List<MessageNode>> siblingsMap)
    {
        if (!node.ParentId.HasValue || !siblingsMap.TryGetValue(node.ParentId.Value, out var siblings))
            return (1, 1, false, false, null, null);

        var sameSiblings = siblings
            .Where(s => s.NodeType == node.NodeType)
            .OrderBy(s => s.CreatedAt)
            .ToList();

        int total = sameSiblings.Count;
        if (total <= 1) return (1, 1, false, false, null, null);

        int idx = sameSiblings.FindIndex(s => s.Id == node.Id);
        if (idx < 0) return (1, total, false, total > 1, null, sameSiblings[0].Id);

        Guid? prevId = idx > 0 ? sameSiblings[idx - 1].Id : null;
        Guid? nextId = idx < total - 1 ? sameSiblings[idx + 1].Id : null;
        return (idx + 1, total, idx > 0, idx < total - 1, prevId, nextId);
    }

    private ToolResultDto MapToolNodeToDto(MessageNode node)
    {
        bool isPending = node.Content == PendingToolSentinel;
        string callId = string.Empty, pluginName = string.Empty, funcName = string.Empty, argsJson = string.Empty;

        if (!string.IsNullOrEmpty(node.Metadata))
        {
            if (isPending)
            {
                // Pending-узел: Metadata содержит PendingToolCallMetadata JSON
                try
                {
                    var meta = JsonSerializer.Deserialize<PendingToolCallMetadata>(node.Metadata, _jsonOptions);
                    if (meta != null)
                    {
                        callId = meta.CallId;
                        pluginName = meta.PluginName;
                        funcName = meta.FunctionName;
                        argsJson = meta.ArgumentsJson;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not parse pending tool metadata for node {NodeId}", node.Id);
                }
            }
            else
            {
                // Выполненный узел: Metadata содержит сериализованный ChatMessageContent
                try
                {
                    var chatMsg = node.GetOrCreateChatMessageContent();
                    var fnResult = chatMsg.Items.OfType<FunctionResultContent>().FirstOrDefault();
                    if (fnResult != null)
                    {
                        callId = fnResult.CallId ?? string.Empty;
                        pluginName = fnResult.PluginName ?? string.Empty;
                        funcName = fnResult.FunctionName ?? string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not extract tool call info from node {NodeId}", node.Id);
                }
            }
        }

        return new ToolResultDto(
            node.Id, node.ParentId, node.CreatedAt,
            callId, pluginName, funcName,
            isPending ? string.Empty : node.Content,
            isPending,
            argsJson);
    }

    /// <summary>
    /// Создаёт Kernel с зарегистрированными MCP tools и базовыми инструментами
    /// </summary>
    private Kernel CreateKernelWithMcpTools()
    {
        var kernel = _kernelFactory.Create();

        var loggingFilter = new FunctionLoggingFilter(
            _loggerFactory.CreateLogger<FunctionLoggingFilter>());
        kernel.FunctionInvocationFilters.Add(loggingFilter);

        var coreToolsPlugin = new CoreToolsPlugin(
            _loggerFactory.CreateLogger<CoreToolsPlugin>());
        kernel.ImportPluginFromObject(coreToolsPlugin, "CoreTools");

        var mcpManagementPlugin = new McpManagementPlugin(
            _loggerFactory.CreateLogger<McpManagementPlugin>(),
            _mcpServerManager,
            _mcpConfigurationService);
        kernel.ImportPluginFromObject(mcpManagementPlugin, "McpManagement");

        var connectedServers = _mcpServerManager.GetConnectedServers();
        if (connectedServers.Count > 0)
        {
            var mcpToolsPlugin = new McpToolsPlugin(
                _mcpServerManager,
                _loggerFactory.CreateLogger<McpToolsPlugin>());
            mcpToolsPlugin.RegisterToolsToKernel(kernel);
        }

        return kernel;
    }
}
