using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

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
    /// TODO: удалить эту перегрузку
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
    /// Добавляет сообщение с полным ChatMessageContent в диалог
    /// </summary>
    public async Task<MessageNode> AddChatMessageAsync(
        Guid conversationId,
        Guid parentNodeId,
        ChatMessageContent chatMessage,
        int tokenCount = 0,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        var parentNode = await _messageNodeRepository.GetByIdAsync(parentNodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Parent message {parentNodeId} not found");

        // Определяем тип узла по роли
        var nodeType = chatMessage.Role.Label.ToLowerInvariant() switch
        {
            "system" => MessageNodeType.System,
            "user" => MessageNodeType.User,
            "assistant" => MessageNodeType.Assistant,
            "tool" => MessageNodeType.Tool,
            _ => MessageNodeType.Assistant
        };

        // Создаем узел с пустым контентом (SetChatMessageContent установит его)
        var message = new MessageNode(conversationId, nodeType, string.Empty, parentNodeId, tokenCount);

        // Используем extension method для сохранения ChatMessageContent
        // (нужно будет добавить using для MessageNodeExtensions)
        message.UpdateContent(chatMessage.Content ?? string.Empty);
        message.SetMetadata(SerializeChatMessage(chatMessage));

        await _messageNodeRepository.AddAsync(message, cancellationToken);

        // Обновляем ActiveChildId родителя и ActiveLeafNodeId диалога
        parentNode.SetActiveChild(message.Id);
        await _messageNodeRepository.UpdateAsync(parentNode, cancellationToken);

        conversation.SetActiveLeafNode(message.Id);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);

        _logger.LogDebug("Added chat message {MessageId} to conversation {ConversationId}",
            message.Id, conversationId);

        return message;
    }

    private string SerializeChatMessage(ChatMessageContent chatMessage)
    {
        // Используем System.Text.Json для сериализации
        // TypeInfoResolver нужен для корректной полиморфной сериализации (FunctionCallContent, FunctionResultContent, etc.)
        return System.Text.Json.JsonSerializer.Serialize(chatMessage, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        });
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
    /// TODO: нигде не используется
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
