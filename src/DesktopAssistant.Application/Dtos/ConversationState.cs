namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Conversation state from the UI perspective — determines the actions available to the user.
/// </summary>
public enum ConversationState
{
    /// <summary>The last message is from the assistant with no tool calls. The user can type.</summary>
    LastMessageIsAssistant = 0,

    /// <summary>The last message is from the user (the LLM has not responded). Show the "Resume" button.</summary>
    LastMessageIsUser,

    /// <summary>All tool calls have been executed, but the LLM has not yet received the results. Show the "Resume" button.</summary>
    AllToolCallsCompleted,

    /// <summary>There are pending tool calls. The user is waiting to approve or deny them.</summary>
    HasPendingToolCalls,

    /// <summary>
    /// Tool result identifiers do not match the tool call identifiers from the assistant.
    /// For example: the result node was not created, or a non-latest result id was provided.
    /// </summary>
    ToolCallIdMismatch,

    /// <summary>
    /// All tool calls completed and at least one of them was a terminal AgentControl tool
    /// (FinishTask or FailTask). The agent loop must NOT be resumed — the task is done.
    /// </summary>
    AgentTaskCompleted,
}
