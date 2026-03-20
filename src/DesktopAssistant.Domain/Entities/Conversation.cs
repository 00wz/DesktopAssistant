using DesktopAssistant.Domain.Enums;

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

    /// <summary>Controls LLM tool-use behavior. Default is Chat (optional tool use).</summary>
    public ConversationMode Mode { get; private set; } = ConversationMode.Chat;

    /// <summary>Parent conversation that spawned this one as a sub-agent. Null for top-level conversations.</summary>
    public Guid? ParentConversationId { get; private set; }

    /// <summary>Tool node in the parent conversation that triggered creation of this sub-agent conversation.</summary>
    public Guid? SpawnedByToolNodeId { get; private set; }

    /// <summary>Whether the LLM in this conversation can spawn child sub-agents.</summary>
    public bool CanSpawnSubagents { get; private set; }

    // Navigation properties
    public AssistantProfile? AssistantProfile { get; private set; }
    public MessageNode? ActiveLeafNode { get; private set; }
    public ICollection<MessageNode> Messages { get; private set; } = new List<MessageNode>();
    public Conversation? ParentConversation { get; private set; }

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

    public void SetMode(ConversationMode mode)
    {
        Mode = mode;
        MarkAsUpdated();
    }

    /// <summary>Marks this conversation as a sub-agent spawned by a specific tool node in a parent conversation.</summary>
    public void SetAsSubagent(Guid parentConversationId, Guid toolNodeId)
    {
        ParentConversationId = parentConversationId;
        SpawnedByToolNodeId = toolNodeId;
        MarkAsUpdated();
    }

    /// <summary>
    /// Updates the tool node that currently "owns" this conversation.
    /// Called when a new send_message_to_subagent tool call takes over the conversation,
    /// so that a retry of the same call can be detected and handled as a resume.
    /// </summary>
    public void UpdateSpawnedByToolNode(Guid toolNodeId)
    {
        SpawnedByToolNodeId = toolNodeId;
        MarkAsUpdated();
    }

    /// <summary>Controls whether the LLM in this conversation can spawn child sub-agents.</summary>
    public void SetCanSpawnSubagents(bool value)
    {
        CanSpawnSubagents = value;
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
