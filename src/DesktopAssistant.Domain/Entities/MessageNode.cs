using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.Domain.Entities;

/// <summary>
/// Узел сообщения в графе истории диалога.
/// Каждое сообщение может иметь родителя и множество дочерних узлов (ветвление).
/// Специальный тип Summary используется для хранения суммаризированного контекста.
/// </summary>
public class MessageNode : BaseEntity
{
    public Guid ConversationId { get; private set; }
    public Guid? ParentId { get; private set; }
    public MessageNodeType NodeType { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public int TokenCount { get; private set; }
    public string? Metadata { get; private set; }
    
    // Навигационные свойства
    public MessageNode? Parent { get; private set; }
    public ICollection<MessageNode> Children { get; private set; } = new List<MessageNode>();
    public Conversation? Conversation { get; private set; }

    private MessageNode() { } // Для EF Core

    public MessageNode(
        Guid conversationId,
        MessageNodeType nodeType,
        string content,
        Guid? parentId = null,
        int tokenCount = 0,
        string? metadata = null)
    {
        ConversationId = conversationId;
        ParentId = parentId;
        NodeType = nodeType;
        Content = content;
        TokenCount = tokenCount;
        Metadata = metadata;
    }

    /// <summary>
    /// Проверяет, является ли узел узлом суммаризации
    /// </summary>
    public bool IsSummaryNode => NodeType == MessageNodeType.Summary;

    public void UpdateContent(string content, int tokenCount = 0)
    {
        Content = content;
        TokenCount = tokenCount;
        MarkAsUpdated();
    }

    public void SetMetadata(string metadata)
    {
        Metadata = metadata;
        MarkAsUpdated();
    }

    /// <summary>
    /// Создаёт дочерний узел (ответвление от текущего сообщения)
    /// </summary>
    public MessageNode CreateChild(MessageNodeType nodeType, string content, int tokenCount = 0, string? metadata = null)
    {
        var child = new MessageNode(ConversationId, nodeType, content, Id, tokenCount, metadata);
        Children.Add(child);
        return child;
    }

    /// <summary>
    /// Создаёт узел суммаризации как дочерний к текущему
    /// </summary>
    public MessageNode CreateSummaryNode(string summaryContent, int tokenCount = 0)
    {
        return CreateChild(MessageNodeType.Summary, summaryContent, tokenCount, 
            metadata: $"{{\"summarizedAt\":\"{DateTime.UtcNow:O}\"}}");
    }
}
