using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.Application.Services;

/// <summary>
/// Сервис для управления диалогами
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
    /// Создаёт новый диалог
    /// </summary>
    public async Task<Conversation> CreateConversationAsync(
        string title,
        Guid assistantProfileId,
        CancellationToken cancellationToken = default)
    {
        var assistant = await _assistantRepository.GetByIdAsync(assistantProfileId, cancellationToken)
            ?? throw new InvalidOperationException($"Assistant profile {assistantProfileId} not found");

        var conversation = new Conversation(title, assistantProfileId);

        // Добавляем системный промпт как корневое сообщение
        var rootMessage = conversation.AddRootMessage(assistant.SystemPrompt);

        await _conversationRepository.AddAsync(conversation, cancellationToken);

        // Устанавливаем ActiveLeafNodeId
        conversation.SetActiveLeafNode(rootMessage.Id);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);

        _logger.LogInformation("Created conversation {ConversationId} with assistant {AssistantId}",
            conversation.Id, assistantProfileId);

        return conversation;
    }

    /// <summary>
    /// Добавляет сообщение в диалог и обновляет активную ветку
    /// </summary>
    public async Task<MessageNode> AddMessageAsync(
        Guid conversationId,
        Guid parentNodeId,
        MessageNodeType nodeType,
        string content,
        int tokenCount = 0,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        var parentNode = await _messageNodeRepository.GetByIdAsync(parentNodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Parent message {parentNodeId} not found");

        var message = new MessageNode(conversationId, nodeType, content, parentNodeId, tokenCount);
        await _messageNodeRepository.AddAsync(message, cancellationToken);

        // Обновляем ActiveChildId родителя и ActiveLeafNodeId диалога
        parentNode.SetActiveChild(message.Id);
        await _messageNodeRepository.UpdateAsync(parentNode, cancellationToken);

        conversation.SetActiveLeafNode(message.Id);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);

        _logger.LogDebug("Added message {MessageId} to conversation {ConversationId}",
            message.Id, conversationId);

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
        var message = await AddMessageAsync(
            conversationId,
            parentNodeId,
            MessageNodeType.Summary,
            summaryContent,
            tokenCount,
            cancellationToken);

        _logger.LogInformation("Created summary node {NodeId} in conversation {ConversationId}",
            message.Id, conversationId);

        return message;
    }

    /// <summary>
    /// Получает путь сообщений от корня до указанного узла (или до Summary Node)
    /// </summary>
    public async Task<IEnumerable<MessageNode>> GetMessagePathAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default)
    {
        return await _messageNodeRepository.GetBranchPathAsync(nodeId, cancellationToken);
    }

    /// <summary>
    /// Собирает контекст для LLM - идёт назад по ветке до Summary Node или корня
    /// </summary>
    public async Task<IEnumerable<MessageNode>> BuildContextAsync(
        Guid headNodeId,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<MessageNode>();
        var currentNode = await _messageNodeRepository.GetByIdAsync(headNodeId, cancellationToken);

        while (currentNode != null)
        {
            messages.Insert(0, currentNode);
            
            // Если нашли Summary Node - останавливаемся
            if (currentNode.IsSummaryNode)
            {
                break;
            }

            if (currentNode.ParentId.HasValue)
            {
                currentNode = await _messageNodeRepository.GetByIdAsync(currentNode.ParentId.Value, cancellationToken);
            }
            else
            {
                currentNode = null;
            }
        }

        return messages;
    }

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

        // Находим новый лист
        var newChild = await _messageNodeRepository.GetByIdAsync(newChildId, cancellationToken)
            ?? throw new InvalidOperationException($"Child node {newChildId} not found");

        // Обновляем активного ребенка родителя
        parentNode.SetActiveChild(newChildId);
        await _messageNodeRepository.UpdateAsync(parentNode, cancellationToken);

        var leafNode = await FindLeafFromNodeAsync(newChild, cancellationToken);

        // Обновляем активный лист диалога
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

    /// <summary>
    /// Получает текущий активный лист диалога
    /// </summary>
    public async Task<MessageNode?> GetActiveLeafNodeAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken);
        if (conversation?.ActiveLeafNodeId == null) return null;

        return await _messageNodeRepository.GetByIdAsync(
            conversation.ActiveLeafNodeId.Value, cancellationToken);
    }
}
