using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.Domain.Entities;

/// <summary>
/// A message node in the conversation history graph.
/// Each message can have a parent and multiple child nodes (branching).
/// The special Summary type is used to store a summarized context.
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

    // Navigation properties
    public MessageNode? Parent { get; private set; }
    public ICollection<MessageNode> Children { get; private set; } = new List<MessageNode>();
    public MessageNode? ActiveChild { get; private set; }
    public Conversation? Conversation { get; private set; }

    private MessageNode() { } // For EF Core

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
    /// Checks whether the node is a summarization node.
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
    /// Sets the active child node.
    /// </summary>
    public void SetActiveChild(Guid childId)
    {
        ActiveChildId = childId;
        MarkAsUpdated();
    }

    /// <summary>
    /// Re-parents the node to a new parent (used when injecting a summary node into the tree).
    /// </summary>
    public void ReparentTo(Guid newParentId)
    {
        ParentId = newParentId;
        MarkAsUpdated();
    }

    /// <summary>
    /// Finds the leaf node by following the ActiveChild chain.
    /// </summary>
    public MessageNode? FindLeafNode()
    {
        var current = this;
        while (current.ActiveChildId.HasValue && current.ActiveChild != null)
            current = current.ActiveChild;
        return current;
    }
}
