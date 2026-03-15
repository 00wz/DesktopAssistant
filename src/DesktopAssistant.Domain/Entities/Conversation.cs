namespace DesktopAssistant.Domain.Entities;

/// <summary>
/// Диалог (чат) с AI-ассистентом.
/// Содержит дерево сообщений с возможностью ветвления.
/// Системный промпт хранится здесь и инжектируется в начало ChatHistory при каждом LLM-тёрне.
/// </summary>
public class Conversation : BaseEntity
{
    public string Title { get; private set; } = string.Empty;
    public Guid? AssistantProfileId { get; private set; }
    public Guid? ActiveLeafNodeId { get; private set; }
    public int TotalTokenCount { get; private set; }

    /// <summary>Системный промпт диалога. Инжектируется первым в ChatHistory если не пустой.</summary>
    public string SystemPrompt { get; private set; } = string.Empty;

    // Навигационные свойства
    public AssistantProfile? AssistantProfile { get; private set; }
    public MessageNode? ActiveLeafNode { get; private set; }
    public ICollection<MessageNode> Messages { get; private set; } = new List<MessageNode>();

    private Conversation() { } // Для EF Core

    public Conversation(string title, Guid assistantProfileId, string systemPrompt = "")
    {
        Title = title;
        AssistantProfileId = assistantProfileId;
        SystemPrompt = systemPrompt;
    }

    public void UpdateTitle(string title)
    {
        Title = title;
        MarkAsUpdated();
    }

    public void UpdateSystemPrompt(string systemPrompt)
    {
        SystemPrompt = systemPrompt;
        MarkAsUpdated();
    }

    public void UpdateTokenCount(int tokenCount)
    {
        TotalTokenCount = tokenCount;
        MarkAsUpdated();
    }

    public void SetActiveLeafNode(Guid nodeId)
    {
        ActiveLeafNodeId = nodeId;
        MarkAsUpdated();
    }

    public void UpdateAssistantProfile(Guid assistantProfileId)
    {
        AssistantProfileId = assistantProfileId;
        MarkAsUpdated();
    }

    /// <summary>
    /// Добавляет якорный корневой узел диалога (пустой, используется как точка входа в дерево).
    /// Системный промпт хранится в Conversation.SystemPrompt и инжектируется через BuildChatHistory.
    /// </summary>
    public MessageNode AddRootMessage()
    {
        var message = new MessageNode(
            Id,
            Enums.MessageNodeType.Root,
            string.Empty,
            null,
            0);

        Messages.Add(message);
        return message;
    }
}
