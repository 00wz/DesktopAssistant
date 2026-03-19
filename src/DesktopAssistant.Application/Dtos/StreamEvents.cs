namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Base type for elements of the IAsyncEnumerable stream from the LLM.
/// Each element of the GetAssistantResponseAsync stream is one of the subtypes.
/// </summary>
public abstract record StreamEvent;

/// <summary>
/// Start of a new assistant turn. One object per turn — not per chunk.
/// </summary>
public sealed record AssistantTurnDto : StreamEvent
{
    public Guid TempId { get; } = Guid.NewGuid();
    public DateTime StartedAt { get; } = DateTime.UtcNow;
}

/// <summary>
/// Text chunk of the current assistant turn.
/// Yielded one at a time for each non-empty chunk from the LLM.
/// </summary>
public sealed record AssistantChunkDto(string Text) : StreamEvent;

/// <summary>
/// Tool call requires user confirmation.
/// PendingNodeId — the node ID in the DB.
/// The consumer creates a card and calls ApproveToolCallAsync / DenyToolCallAsync based on user action.
/// </summary>
public sealed record ToolCallRequestedDto(
    string CallId,
    string PluginName,
    string FunctionName,
    string ArgumentsJson,
    Guid PendingNodeId,
    bool IsTerminal = false) : StreamEvent;

/// <summary>
/// Last element of the turn — the assistant message has been saved to the DB.
/// LastNodeId — the ID of the saved assistant node.
/// If the response contains tool calls, this event arrives BEFORE ToolCallRequestedDto.
/// </summary>
public sealed record AssistantResponseSavedDto(Guid LastNodeId, int InputTokenCount = 0, int OutputTokenCount = 0, int TotalTokenCount = 0) : StreamEvent;
