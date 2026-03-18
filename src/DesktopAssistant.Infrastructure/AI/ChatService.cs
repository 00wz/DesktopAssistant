using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Application.Services;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Domain.Interfaces;
using DesktopAssistant.Infrastructure.AI.Executors;
using DesktopAssistant.Infrastructure.AI.Metadata;
using DesktopAssistant.Infrastructure.AI.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI;

/// <summary>
/// IChatService implementation — conversation management, message history, LLM and tool calls.
/// </summary>
public class ChatService : IChatService
{
    private readonly LlmTurnExecutor _llmTurnExecutor;
    private readonly ToolCallExecutor _toolCallExecutor;
    private readonly ISummarizationService _summarizationService;
    private readonly ConversationService _conversationService;
    private readonly IMessageNodeRepository _messageNodeRepository;
    private readonly IAssistantProfileRepository _assistantRepository;
    private readonly IAppSettingsRepository _appSettingsRepository;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        LlmTurnExecutor llmTurnExecutor,
        ToolCallExecutor toolCallExecutor,
        ISummarizationService summarizationService,
        ConversationService conversationService,
        IMessageNodeRepository messageNodeRepository,
        IAssistantProfileRepository assistantRepository,
        IAppSettingsRepository appSettingsRepository,
        ISecureCredentialStore credentialStore,
        ILogger<ChatService> logger)
    {
        _llmTurnExecutor = llmTurnExecutor;
        _toolCallExecutor = toolCallExecutor;
        _summarizationService = summarizationService;
        _conversationService = conversationService;
        _messageNodeRepository = messageNodeRepository;
        _assistantRepository = assistantRepository;
        _appSettingsRepository = appSettingsRepository;
        _credentialStore = credentialStore;
        _logger = logger;
    }

    // ── Conversation management ──────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ConversationDto> CreateConversationAsync(
        string title,
        Guid? assistantProfileId = null,
        string systemPrompt = "",
        ConversationMode mode = ConversationMode.Chat,
        CancellationToken cancellationToken = default)
    {
        AssistantProfile profile;

        if (assistantProfileId.HasValue)
        {
            profile = await _assistantRepository.GetByIdAsync(assistantProfileId.Value, cancellationToken)
                ?? throw new InvalidOperationException($"Assistant profile {assistantProfileId} not found");
        }
        else
        {
            var defaultIdStr = await _appSettingsRepository.GetValueAsync(
                AppSettings.Keys.DefaultProfileId, cancellationToken);
            if (!Guid.TryParse(defaultIdStr, out var defaultId))
                throw new InvalidOperationException(
                    "No default assistant profile configured. Please set a default profile first.");
            profile = await _assistantRepository.GetByIdAsync(defaultId, cancellationToken)
                ?? throw new InvalidOperationException(
                    "Default assistant profile not found. Please set a valid default profile.");
        }

        var conversation = await _conversationService.CreateConversationAsync(
            title, profile.Id, systemPrompt, mode, cancellationToken);

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
    public async Task<ConversationSettingsDto?> GetConversationSettingsAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationService.GetConversationAsync(conversationId, cancellationToken);
        if (conversation == null) return null;

        AssistantProfileDto? profileDto = null;
        if (conversation.AssistantProfileId.HasValue)
        {
            var profile = await _assistantRepository.GetByIdAsync(conversation.AssistantProfileId.Value, cancellationToken);
            if (profile != null)
                profileDto = await MapToProfileDtoAsync(profile, cancellationToken);
        }

        return new ConversationSettingsDto(
            conversationId,
            conversation.SystemPrompt,
            conversation.AssistantProfileId,
            profileDto,
            conversation.Mode);
    }

    /// <inheritdoc />
    public async Task UpdateConversationSystemPromptAsync(
        Guid conversationId,
        string systemPrompt,
        CancellationToken cancellationToken = default)
    {
        await _conversationService.UpdateSystemPromptAsync(conversationId, systemPrompt, cancellationToken);
        _logger.LogInformation("Updated system prompt for conversation {ConversationId}", conversationId);
    }

    /// <inheritdoc />
    public async Task ChangeConversationProfileAsync(
        Guid conversationId,
        Guid newProfileId,
        CancellationToken cancellationToken = default)
    {
        await _conversationService.UpdateAssistantProfileAsync(conversationId, newProfileId, cancellationToken);

        _logger.LogInformation("Changed profile for conversation {ConversationId} to {ProfileId}",
            conversationId, newProfileId);
    }

    /// <inheritdoc />
    public async Task ChangeConversationModeAsync(
        Guid conversationId,
        ConversationMode mode,
        CancellationToken cancellationToken = default)
    {
        await _conversationService.UpdateModeAsync(conversationId, mode, cancellationToken);

        _logger.LogInformation("Changed mode for conversation {ConversationId} to {Mode}",
            conversationId, mode);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<MessageDto>> GetConversationHistoryAsync(
        Guid conversationId,
        Guid lastNodeId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationService.GetConversationAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        var lastNode = await _messageNodeRepository.GetByIdAsync(lastNodeId)
            ?? throw new InvalidOperationException($"MessageNode {lastNodeId} not found");

        var nodes = (await _conversationService.GetBranchPathAsync(
            lastNodeId, cancellationToken)).ToList();

        var visibleNodes = nodes.Where(n => n.NodeType != MessageNodeType.Root).ToList();

        var parentIds = visibleNodes
            .Where(n => n.ParentId.HasValue)
            .Select(n => n.ParentId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, List<MessageNode>> siblingsMap = [];
        foreach (var parentId in parentIds)
        {
            var siblings = (await _messageNodeRepository.GetChildrenAsync(parentId, cancellationToken)).ToList();
            siblingsMap[parentId] = siblings;
        }

        return [.. visibleNodes.Select(n => MapNodeToDto(n, siblingsMap))];
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
            //.Where(s => s.NodeType == MessageNodeType.User)
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
        bool hasPending = false;
        bool hasTerminal = false;
        var seenCallIds = new HashSet<string>();

        await foreach (var node in _messageNodeRepository.TraverseToRootAsync(lastNodeId, cancellationToken))
        {
            if (node.NodeType == MessageNodeType.Tool)
            {
                var meta = ToolNodeMetadata.TryDeserialize(node.Metadata);
                if (meta != null)
                {
                    seenCallIds.Add(meta.CallId);
                    if (meta.ResultJson == null)
                        hasPending = true;
                    else if (meta.IsTerminal)
                        hasTerminal = true;
                }
                continue;
            }

            if (node.NodeType == MessageNodeType.Assistant)
            {
                var toolCallIds = ExtractToolCallIds(node);

                if (seenCallIds.Count == 0 && toolCallIds.Count == 0)
                    return ConversationState.LastMessageIsAssistant;

                if (!toolCallIds.SetEquals(seenCallIds))
                    return ConversationState.ToolCallIdMismatch;

                break;
            }

            if (seenCallIds.Count > 0)
                throw new InvalidOperationException(
                    $"Unexpected node type {node.NodeType} (id {node.Id}) encountered after tool result nodes.");

            switch (node.NodeType)
            {
                case MessageNodeType.User:
                    return ConversationState.LastMessageIsUser;
                /// nodes of types <see cref="MessageNodeType.Root"/> and <see cref="MessageNodeType.Summary"/> are treated as assistant messages for now.
                default:
                    return ConversationState.LastMessageIsAssistant;
            }
        }

        if (hasPending) return ConversationState.HasPendingToolCalls;
        return hasTerminal ? ConversationState.AgentTaskCompleted : ConversationState.AllToolCallsCompleted;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SummarizationEvent> SummarizeAsync(
        Guid conversationId,
        Guid selectedNodeId,
        CancellationToken cancellationToken = default)
        => _summarizationService.SummarizeAsync(conversationId, selectedNodeId, cancellationToken);

    private static HashSet<string> ExtractToolCallIds(MessageNode node)
    {
        if (!ChatMessageSerializer.TryDeserialize(node.Metadata, out var msg) || msg == null)
            return [];
        return msg.Items
            .OfType<FunctionCallContent>()
            .Select(fc => fc.Id)
            .Where(id => id != null)
            .ToHashSet()!;
    }

    // ── DTO mapping ─────────────────────────────────────────────────────────

    private static ConversationDto MapToConversationDto(Conversation c) =>
        new(c.Id, c.Title, c.ActiveLeafNodeId, c.CreatedAt, c.UpdatedAt);

    private async Task<AssistantProfileDto> MapToProfileDtoAsync(
        AssistantProfile p, CancellationToken cancellationToken)
    {
        var defaultIdStr = await _appSettingsRepository.GetValueAsync(
            AppSettings.Keys.DefaultProfileId, cancellationToken);
        Guid.TryParse(defaultIdStr, out var defaultId);
        var summarizationIdStr = await _appSettingsRepository.GetValueAsync(
            AppSettings.Keys.SummarizationProfileId, cancellationToken);
        Guid.TryParse(summarizationIdStr, out var summarizationId);
        return new(p.Id, p.Description, p.BaseUrl, p.ModelId, p.Temperature, p.MaxTokens, p.Id == defaultId,
            _credentialStore.HasApiKey(p.Id), p.Id == summarizationId);
    }

    private static MessageDto MapNodeToDto(MessageNode node, Dictionary<Guid, List<MessageNode>> siblingsMap)
    {
        var (currentIndex, total, hasPrev, hasNext, prevId, nextId) = ComputeSiblingInfo(node, siblingsMap);

        return node.NodeType switch
        {
            MessageNodeType.User => new UserMessageDto(
                node.Id, node.ParentId, node.CreatedAt, node.Content,
                currentIndex, total, hasPrev, hasNext, prevId, nextId),

            MessageNodeType.Assistant => MapAssistantNodeToDto(node, currentIndex, total, hasPrev, hasNext, prevId, nextId),

            MessageNodeType.Tool => MapToolNodeToDto(node),

            MessageNodeType.Summary => MapSummaryNodeToDto(node),

            _ => new AssistantMessageDto(node.Id, node.ParentId, node.CreatedAt, node.Content)
        };
    }

    private static (int currentIndex, int total, bool hasPrev, bool hasNext, Guid? prevId, Guid? nextId)
        ComputeSiblingInfo(MessageNode node, Dictionary<Guid, List<MessageNode>> siblingsMap)
    {
        if (!node.ParentId.HasValue || !siblingsMap.TryGetValue(node.ParentId.Value, out var siblings))
            return (1, 1, false, false, null, null);

        var sameSiblings = siblings
            //.Where(s => s.NodeType == node.NodeType)
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

    private static AssistantMessageDto MapAssistantNodeToDto(
        MessageNode node, int currentIndex, int total, bool hasPrev, bool hasNext, Guid? prevId, Guid? nextId)
    {
        var (inputTokenCount, outputTokenCount, totalTokenCount) = (0, 0, 0);
        if (ChatMessageSerializer.TryDeserialize(node.Metadata, out var msg) && msg != null)
            (inputTokenCount, outputTokenCount, totalTokenCount) = TokenUsageHelper.Extract(msg);

        return new AssistantMessageDto(
            node.Id, node.ParentId, node.CreatedAt, node.Content,
            currentIndex, total, hasPrev, hasNext, prevId, nextId,
            inputTokenCount, outputTokenCount, totalTokenCount);
    }

    private static SummaryMessageDto MapSummaryNodeToDto(MessageNode node)
    {
        return new SummaryMessageDto(node.Id, node.ParentId, node.CreatedAt, node.Content);
    }

    private static ToolResultDto MapToolNodeToDto(MessageNode node)
    {
        var meta = ToolNodeMetadata.TryDeserialize(node.Metadata)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize ToolNodeMetadata for node {node.Id}");

        return new ToolResultDto(
            node.Id, node.ParentId, node.CreatedAt,
            meta.CallId, meta.PluginName, meta.FunctionName,
            meta.ResultJson ?? string.Empty,
            Status: meta.Status,
            meta.ArgumentsJson);
    }
}
