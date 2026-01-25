namespace DesktopAssistant.Domain.Entities;

/// <summary>
/// Диалог (чат) с AI-ассистентом.
/// Содержит дерево сообщений с возможностью ветвления.
/// </summary>
public class Conversation : BaseEntity
{
    public string Title { get; private set; } = string.Empty;
    public Guid AssistantProfileId { get; private set; }
    public Guid? ActiveBranchId { get; private set; }
    public string? Summary { get; private set; }
    public int TotalTokenCount { get; private set; }
    
    // Навигационные свойства
    public AssistantProfile? AssistantProfile { get; private set; }
    public ConversationBranch? ActiveBranch { get; private set; }
    public ICollection<MessageNode> Messages { get; private set; } = new List<MessageNode>();
    public ICollection<ConversationBranch> Branches { get; private set; } = new List<ConversationBranch>();

    private Conversation() { } // Для EF Core

    public Conversation(string title, Guid assistantProfileId)
    {
        Title = title;
        AssistantProfileId = assistantProfileId;
    }

    public void UpdateTitle(string title)
    {
        Title = title;
        MarkAsUpdated();
    }

    public void SetSummary(string summary)
    {
        Summary = summary;
        MarkAsUpdated();
    }

    public void UpdateTokenCount(int tokenCount)
    {
        TotalTokenCount = tokenCount;
        MarkAsUpdated();
    }

    public void SetActiveBranch(Guid branchId)
    {
        ActiveBranchId = branchId;
        MarkAsUpdated();
    }

    /// <summary>
    /// Добавляет корневое сообщение (system prompt) в диалог
    /// </summary>
    public MessageNode AddRootMessage(string systemPrompt, int tokenCount = 0)
    {
        var message = new MessageNode(
            Id,
            Enums.MessageNodeType.System,
            systemPrompt,
            null,
            tokenCount);
        
        Messages.Add(message);
        return message;
    }
}
