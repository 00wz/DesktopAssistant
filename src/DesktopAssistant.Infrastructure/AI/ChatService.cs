using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Application.Services;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Domain.Interfaces;
using DesktopAssistant.Infrastructure.AI.Extensions;
using DesktopAssistant.Infrastructure.AI.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI;

/// <summary>
/// Фасад IChatService — оркестрирует ConversationService, LlmTurnExecutor и ToolCallExecutor.
/// Отвечает за управление диалогами, маппинг DTO и делегирование LLM/tool-операций.
/// </summary>
public class ChatService : IChatService
{
    private readonly LlmTurnExecutor _llmTurnExecutor;
    private readonly ToolCallExecutor _toolCallExecutor;
    private readonly ConversationService _conversationService;
    private readonly IConversationRepository _conversationRepository;
    private readonly IMessageNodeRepository _messageNodeRepository;
    private readonly IAssistantProfileRepository _assistantRepository;
    private readonly LlmOptions _llmOptions;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        LlmTurnExecutor llmTurnExecutor,
        ToolCallExecutor toolCallExecutor,
        ConversationService conversationService,
        IConversationRepository conversationRepository,
        IMessageNodeRepository messageNodeRepository,
        IAssistantProfileRepository assistantRepository,
        IOptions<LlmOptions> llmOptions,
        ILogger<ChatService> logger)
    {
        _llmTurnExecutor = llmTurnExecutor;
        _toolCallExecutor = toolCallExecutor;
        _conversationService = conversationService;
        _conversationRepository = conversationRepository;
        _messageNodeRepository = messageNodeRepository;
        _assistantRepository = assistantRepository;
        _llmOptions = llmOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ConversationDto> CreateConversationAsync(
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
            title, assistant.Id, cancellationToken);

        _logger.LogInformation("Created conversation {ConversationId}: {Title}", conversation.Id, title);
        return MapToConversationDto(conversation);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ConversationDto>> GetConversationsAsync(
        CancellationToken cancellationToken = default)
    {
        var conversations = await _conversationService.GetActiveConversationsAsync(cancellationToken);
        return conversations.Select(MapToConversationDto);
    }

    /// <inheritdoc />
    public async Task<ConversationDto?> GetConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationService.GetConversationAsync(conversationId, cancellationToken);
        return conversation == null ? null : MapToConversationDto(conversation);
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

        var visibleNodes = nodes.Where(n => n.NodeType != MessageNodeType.System).ToList();

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
            conversationId, parentNodeId, newChildId, cancellationToken);

        _logger.LogDebug("Switched to sibling {NewChildId} in conversation {ConversationId}",
            newChildId, conversationId);
    }

    /// <inheritdoc />
    public async Task<UserMessageDto> AddUserMessageAsync(
        Guid conversationId,
        Guid parentNodeId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var userMessage = new ChatMessageContent(AuthorRole.User, content);
        var metadata = ChatMessageSerializer.Serialize(userMessage);

        var userNode = await _conversationService.AddNodeAsync(
            conversationId, parentNodeId, MessageNodeType.User, content, metadata,
            cancellationToken: cancellationToken);

        _logger.LogInformation("[USER MESSAGE] {MessageId}:\n{Content}", userNode.Id, content);

        var siblings = (await _messageNodeRepository.GetChildrenAsync(parentNodeId, cancellationToken)).ToList();
        var userSiblings = siblings
            .Where(s => s.NodeType == MessageNodeType.User)
            .OrderBy(s => s.CreatedAt)
            .ToList();

        int total = userSiblings.Count;
        int idx = userSiblings.FindIndex(s => s.Id == userNode.Id);
        Guid? prevId = idx > 0 ? userSiblings[idx - 1].Id : null;
        Guid? nextId = idx < total - 1 ? userSiblings[idx + 1].Id : null;

        return new UserMessageDto(
            userNode.Id, userNode.ParentId, userNode.CreatedAt, userNode.Content,
            idx >= 0 ? idx + 1 : 1, total,
            idx > 0, idx < total - 1,
            prevId, nextId);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<StreamEvent> GetAssistantResponseAsync(
        Guid conversationId,
        Guid lastMessageId,
        CancellationToken cancellationToken = default)
        => _llmTurnExecutor.ExecuteAsync(conversationId, lastMessageId, cancellationToken);

    /// <inheritdoc />
    public Task<ToolCallResult> ApproveToolCallAsync(
        Guid pendingNodeId,
        CancellationToken cancellationToken = default)
        => _toolCallExecutor.ApproveAsync(pendingNodeId, cancellationToken);

    /// <inheritdoc />
    public Task<ToolCallResult> DenyToolCallAsync(
        Guid pendingNodeId,
        CancellationToken cancellationToken = default)
        => _toolCallExecutor.DenyAsync(pendingNodeId, cancellationToken);

    /// <inheritdoc />
    public async Task<ConversationState> GetConversationStateAsync(
        Guid lastNodeId,
        CancellationToken cancellationToken = default)
    {
        bool foundTools = false;
        bool hasPending = false;

        await foreach (var node in _messageNodeRepository.TraverseToRootAsync(lastNodeId, cancellationToken))
        {
            if (node.NodeType != MessageNodeType.Tool)
            {
                if (!foundTools)
                {
                    return node.NodeType == MessageNodeType.User
                        ? ConversationState.LastMessageIsUser
                        : ConversationState.LastMessageIsAssistant;
                }
                break;
            }

            foundTools = true;
            var meta = ToolNodeMetadata.TryDeserialize(node.Metadata);
            if (meta != null && meta.ResultJson == null)
                hasPending = true;
        }

        return hasPending ? ConversationState.HasPendingToolCalls : ConversationState.AllToolCallsCompleted;
    }

    // ── DTO mapping ─────────────────────────────────────────────────────────

    private static ConversationDto MapToConversationDto(Conversation c) =>
        new(c.Id, c.Title, c.ActiveLeafNodeId, c.CreatedAt, c.UpdatedAt);

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

    private static (int currentIndex, int total, bool hasPrev, bool hasNext, Guid? prevId, Guid? nextId)
        ComputeSiblingInfo(MessageNode node, Dictionary<Guid, List<MessageNode>> siblingsMap)
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

    /// <summary>
    /// Маппит tool-узел в ToolResultDto.
    /// Новый формат (ToolNodeMetadata): данные из метаданных.
    /// Старый формат (ChatMessageContent): извлекает FunctionResultContent из Items.
    /// </summary>
    private ToolResultDto MapToolNodeToDto(MessageNode node)
    {
        var meta = ToolNodeMetadata.TryDeserialize(node.Metadata);

        if (meta != null)
        {
            return new ToolResultDto(
                node.Id, node.ParentId, node.CreatedAt,
                meta.CallId, meta.PluginName, meta.FunctionName,
                meta.ResultJson ?? string.Empty,
                IsPending: meta.ResultJson == null,
                meta.ArgumentsJson);
        }

        // Старый формат: извлекаем FunctionResultContent из сериализованного ChatMessageContent
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
            ResultJson: node.Content);
    }
}
