namespace DesktopAssistant.Domain.Entities;

/// <summary>
/// Conversation (chat) with an AI assistant.
/// Contains a message tree with branching support.
/// The system prompt is stored here and injected at the start of ChatHistory on each LLM turn.
/// </summary>
public class Conversation : BaseEntity
{
    public string Title { get; private set; } = string.Empty;
    public Guid? AssistantProfileId { get; private set; }
    public Guid? ActiveLeafNodeId { get; private set; }
    public int TotalTokenCount { get; private set; }

    /// <summary>Conversation system prompt. Injected first into ChatHistory if non-empty.</summary>
    public string SystemPrompt { get; private set; } = string.Empty;

    // Navigation properties
    public AssistantProfile? AssistantProfile { get; private set; }
    public MessageNode? ActiveLeafNode { get; private set; }
    public ICollection<MessageNode> Messages { get; private set; } = new List<MessageNode>();

    private Conversation() { } // For EF Core

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
    /// Adds an anchor root node to the conversation (empty, used as an entry point into the tree).
    /// The system prompt is stored in Conversation.SystemPrompt and injected via BuildChatHistory.
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
