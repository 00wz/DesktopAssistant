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

        var nodes = (await _conversationService.BuildContextAsync(
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

        return new UserMessageDto(
            userNode.Id,
            userNode.ParentId,
            userNode.CreatedAt,
            userNode.Content,
            currentIndex,
            total,
            idx > 0,
            idx < total - 1);
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
        var initialMessageCount = chatHistory.Count;

        var kernel = CreateKernelWithMcpTools();
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false)
        };

        // Цикл обработки LLM-тёрнов с tool calls
        while (!cancellationToken.IsCancellationRequested)
        {
            var turn = new AssistantTurnDto();
            yield return turn;

            // Стримим ответ LLM и уведомляем через события DTO
            var aggregator = new StreamingChatMessageAggregator();
            try
            {
                await foreach (var chunk in chatCompletionService.GetStreamingChatMessageContentsAsync(
                    chatHistory, executionSettings, kernel, cancellationToken))
                {
                    aggregator.Append(chunk);

                    if (!string.IsNullOrEmpty(chunk.Content))
                        turn.OnChunk(chunk.Content);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting streaming response from LLM");
                throw;
            }

            // Сигнал завершения тёрна — вызывается ПЕРЕД yield следующего элемента
            turn.OnCompleted();

            var assistantMessage = aggregator.Build();
            chatHistory.Add(assistantMessage);

            var functionCalls = FunctionCallContent.GetFunctionCalls(assistantMessage).ToList();

            if (!functionCalls.Any())
            {
                _logger.LogInformation("[ASSISTANT MESSAGE] Response ({Length} chars):\n{Content}",
                    assistantMessage.Content?.Length ?? 0, assistantMessage.Content);
                break;
            }

            _logger.LogInformation("[FUNCTION CALLS] LLM requested {Count} function calls", functionCalls.Count);

            foreach (var functionCall in functionCalls)
            {
                var callId = functionCall.Id ?? Guid.NewGuid().ToString();
                var argsJson = SerializeFunctionArgs(functionCall);
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                yield return new ToolCallRequestedDto(
                    callId,
                    functionCall.PluginName ?? string.Empty,
                    functionCall.FunctionName,
                    argsJson,
                    tcs);

                // Producer ждёт решения пользователя; WaitAsync освобождает поток при отмене CT
                bool approved = await tcs.Task.WaitAsync(cancellationToken);

                if (!approved)
                {
                    _logger.LogInformation("[TOOL DENIED] {FunctionName} was denied by user", functionCall.FunctionName);
                    chatHistory.Add(new FunctionResultContent(functionCall, "Denied by user.").ToChatMessage());
                    yield return new ToolCallFailedDto(callId, "Denied by user");
                    continue;
                }

                yield return new ToolCallExecutingDto(callId);

                // yield нельзя использовать внутри try/catch — вычисляем результат отдельно
                FunctionResultContent resultContent;
                string? invokeError = null;
                try
                {
                    _logger.LogDebug("[FUNCTION CALL] Invoking {PluginName}.{FunctionName}",
                        functionCall.PluginName, functionCall.FunctionName);

                    resultContent = await functionCall.InvokeAsync(kernel, cancellationToken);

                    _logger.LogDebug("[FUNCTION RESULT] {PluginName}.{FunctionName} returned: {Result}",
                        functionCall.PluginName, functionCall.FunctionName, resultContent.Result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[FUNCTION ERROR] {PluginName}.{FunctionName} failed",
                        functionCall.PluginName, functionCall.FunctionName);
                    resultContent = new FunctionResultContent(functionCall, ex);
                    invokeError = ex.Message;
                }

                chatHistory.Add(resultContent.ToChatMessage());

                if (invokeError == null)
                    yield return new ToolCallCompletedDto(callId, resultContent.Result?.ToString() ?? string.Empty);
                else
                    yield return new ToolCallFailedDto(callId, invokeError);
            }
        }

        // Сохраняем все новые сообщения в БД (выполняется когда consumer вызывает последний MoveNextAsync)
        var newMessages = chatHistory.Skip(initialMessageCount).ToList();

        if (newMessages.Count == 0)
            throw new InvalidOperationException("No new messages were generated by LLM");

        MessageNode? lastNode = null;
        foreach (var message in newMessages)
        {
            var parentId = lastNode?.Id ?? lastMessageId.Value;
            lastNode = await _conversationService.AddChatMessageAsync(
                conversationId,
                parentId,
                message,
                cancellationToken: cancellationToken);

            _logger.LogDebug("[SAVED MESSAGE] {Role} message {MessageId}, parent: {ParentId}",
                message.Role.Label, lastNode.Id, parentId);
        }

        // Уведомляем consumer об ID последнего сохранённого узла
        if (lastNode != null)
            yield return new AssistantResponseSavedDto(lastNode.Id);
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
        var (currentIndex, total, hasPrev, hasNext) = ComputeSiblingInfo(node, siblingsMap);

        return node.NodeType switch
        {
            MessageNodeType.User => new UserMessageDto(
                node.Id, node.ParentId, node.CreatedAt, node.Content,
                currentIndex, total, hasPrev, hasNext),

            MessageNodeType.Assistant => new AssistantMessageDto(
                node.Id, node.ParentId, node.CreatedAt, node.Content,
                currentIndex, total, hasPrev, hasNext),

            MessageNodeType.Tool => MapToolNodeToDto(node),

            MessageNodeType.Summary => new SummaryMessageDto(
                node.Id, node.ParentId, node.CreatedAt, node.Content, 0, 0),

            _ => new AssistantMessageDto(node.Id, node.ParentId, node.CreatedAt, node.Content)
        };
    }

    private static (int currentIndex, int total, bool hasPrev, bool hasNext) ComputeSiblingInfo(
        MessageNode node, Dictionary<Guid, List<MessageNode>> siblingsMap)
    {
        if (!node.ParentId.HasValue || !siblingsMap.TryGetValue(node.ParentId.Value, out var siblings))
            return (1, 1, false, false);

        var sameSiblings = siblings
            .Where(s => s.NodeType == node.NodeType)
            .OrderBy(s => s.CreatedAt)
            .ToList();

        int total = sameSiblings.Count;
        if (total <= 1) return (1, 1, false, false);

        int idx = sameSiblings.FindIndex(s => s.Id == node.Id);
        if (idx < 0) return (1, total, false, total > 1);

        return (idx + 1, total, idx > 0, idx < total - 1);
    }

    private ToolResultDto MapToolNodeToDto(MessageNode node)
    {
        string callId = string.Empty, pluginName = string.Empty, funcName = string.Empty;

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

        return new ToolResultDto(
            node.Id, node.ParentId, node.CreatedAt,
            callId, pluginName, funcName,
            node.Content);
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
