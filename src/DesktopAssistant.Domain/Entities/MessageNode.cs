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
    public Guid? ActiveChildId { get; private set; }

    // Навигационные свойства
    public MessageNode? Parent { get; private set; }
    public ICollection<MessageNode> Children { get; private set; } = new List<MessageNode>();
    public MessageNode? ActiveChild { get; private set; }
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
    /// Устанавливает активного дочернего узла
    /// </summary>
    public void SetActiveChild(Guid childId)
    {
        ActiveChildId = childId;
        MarkAsUpdated();
    }

    /// <summary>
    /// Перепривязывает узел к новому родителю (используется при инжекции summary-узла в дерево)
    /// </summary>
    public void ReparentTo(Guid newParentId)
    {
        ParentId = newParentId;
        MarkAsUpdated();
    }

    /// <summary>
    /// Находит листовой узел, следуя по цепочке ActiveChild
    /// </summary>
    public MessageNode? FindLeafNode()
    {
        var current = this;
        while (current.ActiveChildId.HasValue && current.ActiveChild != null)
            current = current.ActiveChild;
        return current;
    }
}
