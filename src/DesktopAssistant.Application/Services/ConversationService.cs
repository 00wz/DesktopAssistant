using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.Application.Services;

/// <summary>
/// Сервис для управления деревом узлов диалога.
/// Не зависит от LLM-инфраструктуры (Semantic Kernel).
/// </summary>
public class ConversationService
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IMessageNodeRepository _messageNodeRepository;
    private readonly IAssistantProfileRepository _assistantRepository;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(
        IConversationRepository conversationRepository,
        IMessageNodeRepository messageNodeRepository,
        IAssistantProfileRepository assistantRepository,
        ILogger<ConversationService> logger)
    {
        _conversationRepository = conversationRepository;
        _messageNodeRepository = messageNodeRepository;
        _assistantRepository = assistantRepository;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт новый диалог с якорным корневым узлом.
    /// Системный промпт хранится в Conversation.SystemPrompt и инжектируется в BuildChatHistory.
    /// </summary>
    public async Task<Conversation> CreateConversationAsync(
        string title,
        Guid assistantProfileId,
        string systemPrompt = "",
        CancellationToken cancellationToken = default)
    {
        _ = await _assistantRepository.GetByIdAsync(assistantProfileId, cancellationToken)
            ?? throw new InvalidOperationException($"Assistant profile {assistantProfileId} not found");

        var conversation = new Conversation(title, assistantProfileId, systemPrompt);

        // Добавляем якорный корневой узел (пустой System-узел как точка входа в дерево)
        var rootMessage = conversation.AddRootMessage();

        await _conversationRepository.AddAsync(conversation, cancellationToken);

        // Устанавливаем ActiveLeafNodeId
        conversation.SetActiveLeafNode(rootMessage.Id);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);

        _logger.LogInformation("Created conversation {ConversationId} with assistant {AssistantId}",
            conversation.Id, assistantProfileId);

        return conversation;
    }

    /// <summary>
    /// Добавляет узел в дерево диалога. Не зависит от SK-типов.
    /// metadata — предсериализованная строка (или null).
    /// </summary>
    public async Task<MessageNode> AddNodeAsync(
        Guid conversationId,
        Guid parentNodeId,
        MessageNodeType nodeType,
        string content,
        string? metadata = null,
        int tokenCount = 0,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        var parentNode = await _messageNodeRepository.GetByIdAsync(parentNodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Parent message {parentNodeId} not found");

        var message = new MessageNode(conversationId, nodeType, content, parentNodeId, tokenCount);
        if (metadata != null) message.SetMetadata(metadata);

        await _messageNodeRepository.AddAsync(message, cancellationToken);

        parentNode.SetActiveChild(message.Id);
        await _messageNodeRepository.UpdateAsync(parentNode, cancellationToken);

        conversation.SetActiveLeafNode(message.Id);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);

        _logger.LogDebug("Added node {NodeId} ({NodeType}) to conversation {ConversationId}",
            message.Id, nodeType, conversationId);

        return message;
    }

    /// <summary>
    /// Добавляет узел суммаризации в диалог
    /// </summary>
    public async Task<MessageNode> AddSummaryNodeAsync(
        Guid conversationId,
        Guid parentNodeId,
        string summaryContent,
        int tokenCount = 0,
        CancellationToken cancellationToken = default)
    {
        var message = await AddNodeAsync(
            conversationId,
            parentNodeId,
            MessageNodeType.Summary,
            summaryContent,
            metadata: null,
            tokenCount,
            cancellationToken);

        _logger.LogInformation("Created summary node {NodeId} in conversation {ConversationId}",
            message.Id, conversationId);

        return message;
    }

    /// <summary>
    /// Возвращает полный упорядоченный путь от корня до указанного узла
    /// </summary>
    public async Task<IEnumerable<MessageNode>> GetBranchPathAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default)
    {
        var path = new List<MessageNode>();
        await foreach (var node in _messageNodeRepository.TraverseToRootAsync(nodeId, cancellationToken))
            path.Add(node);
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Собирает контекст для LLM - идёт назад по ветке до Summary Node или корня
    /// </summary>
    public async Task<IEnumerable<MessageNode>> BuildContextAsync(
        Guid headNodeId,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<MessageNode>();
        await foreach (var node in _messageNodeRepository.TraverseToRootAsync(headNodeId, cancellationToken))
        {
            messages.Add(node);
            if (node.IsSummaryNode)
                break;
        }
        messages.Reverse();
        return messages;
    }

    /// <summary>
    /// Возвращает системный промпт диалога
    /// </summary>
    public async Task<string> GetSystemPromptAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken);
        return conversation?.SystemPrompt ?? string.Empty;
    }

    /// <summary>
    /// Обновляет системный промпт диалога
    /// </summary>
    public async Task UpdateSystemPromptAsync(
        Guid conversationId,
        string systemPrompt,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        conversation.UpdateSystemPrompt(systemPrompt);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);

        _logger.LogInformation("Updated system prompt for conversation {ConversationId}", conversationId);
    }

    /// <summary>
    /// Возвращает профиль ассистента для указанного диалога
    /// </summary>
    public async Task<AssistantProfile?> GetAssistantProfileAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken);
        if (conversation == null) return null;
        return await _assistantRepository.GetByIdAsync(conversation.AssistantProfileId, cancellationToken);
    }

    /// <summary>
    /// Получает диалог по ID
    /// </summary>
    public async Task<Conversation?> GetConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
        => await _conversationRepository.GetByIdAsync(conversationId, cancellationToken);

    /// <summary>
    /// Получает все активные диалоги
    /// </summary>
    public async Task<IEnumerable<Conversation>> GetActiveConversationsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _conversationRepository.GetActiveConversationsAsync(cancellationToken);
    }

    /// <summary>
    /// Soft-delete диалога
    /// </summary>
    public async Task DeleteConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        await _conversationRepository.SoftDeleteAsync(conversationId, cancellationToken);
        _logger.LogInformation("Soft-deleted conversation {ConversationId}", conversationId);
    }

    /// <summary>
    /// Переключается на sibling сообщение
    /// </summary>
    public async Task SwitchToSiblingAsync(
        Guid conversationId,
        Guid parentNodeId,
        Guid newChildId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        var parentNode = await _messageNodeRepository.GetByIdAsync(parentNodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Parent node {parentNodeId} not found");

        var newChild = await _messageNodeRepository.GetByIdAsync(newChildId, cancellationToken)
            ?? throw new InvalidOperationException($"Child node {newChildId} not found");

        parentNode.SetActiveChild(newChildId);
        await _messageNodeRepository.UpdateAsync(parentNode, cancellationToken);

        var leafNode = await FindLeafFromNodeAsync(newChild, cancellationToken);

        conversation.SetActiveLeafNode(leafNode.Id);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);

        _logger.LogInformation(
            "Switched to sibling {ChildId} in conversation {ConversationId}, new leaf: {LeafId}",
            newChildId, conversationId, leafNode.Id);
    }

    /// <summary>
    /// Находит лист, следуя по цепочке ActiveChildId
    /// </summary>
    private async Task<MessageNode> FindLeafFromNodeAsync(
        MessageNode startNode,
        CancellationToken cancellationToken)
    {
        var current = startNode;
        while (current.ActiveChildId.HasValue)
        {
            current = await _messageNodeRepository.GetByIdAsync(
                current.ActiveChildId.Value, cancellationToken)
                ?? throw new InvalidOperationException($"Active child not found");
        }
        return current;
    }
}
