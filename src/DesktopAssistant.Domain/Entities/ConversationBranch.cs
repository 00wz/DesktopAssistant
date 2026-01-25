namespace DesktopAssistant.Domain.Entities;

/// <summary>
/// Ветка диалога - представляет путь в дереве сообщений от корня до текущего активного узла.
/// Позволяет переключаться между разными ветками истории диалога.
/// </summary>
public class ConversationBranch : BaseEntity
{
    public Guid ConversationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid HeadNodeId { get; private set; }
    public bool IsDefault { get; private set; }
    
    // Навигационные свойства
    public Conversation? Conversation { get; private set; }
    public MessageNode? HeadNode { get; private set; }

    private ConversationBranch() { } // Для EF Core

    public ConversationBranch(Guid conversationId, string name, Guid headNodeId, bool isDefault = false)
    {
        ConversationId = conversationId;
        Name = name;
        HeadNodeId = headNodeId;
        IsDefault = isDefault;
    }

    public void UpdateName(string name)
    {
        Name = name;
        MarkAsUpdated();
    }

    public void SetHeadNode(Guid nodeId)
    {
        HeadNodeId = nodeId;
        MarkAsUpdated();
    }

    public void SetAsDefault()
    {
        IsDefault = true;
        MarkAsUpdated();
    }
}
