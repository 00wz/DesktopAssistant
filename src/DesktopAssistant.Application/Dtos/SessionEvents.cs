namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Base type for events published by <c>IConversationSession</c> to subscribers.
/// </summary>
public abstract record SessionEvent;

/// <summary>The session started or finished automatic processing (streaming / auto-approve tools).</summary>
public sealed record RunningStateChangedSessionEvent(bool IsRunning) : SessionEvent;

/// <summary>The conversation state changed (e.g. after all tool calls have completed).</summary>
public sealed record ConversationStateChangedSessionEvent(ConversationState State) : SessionEvent;

/// <summary>The user sent a message — node saved to DB.</summary>
public sealed record UserMessageAddedSessionEvent(UserMessageDto Dto) : SessionEvent;

/// <summary>The LLM started a new response turn.</summary>
public sealed record AssistantTurnStartedSessionEvent(Guid TempId, DateTime StartedAt) : SessionEvent;

/// <summary>Text chunk of the current assistant turn.</summary>
public sealed record AssistantChunkSessionEvent(string Text) : SessionEvent;

/// <summary>The assistant turn completed and was saved to DB.</summary>
public sealed record AssistantResponseSavedSessionEvent(Guid LastNodeId, int TotalTokenCount) : SessionEvent;

/// <summary>The LLM is invoking a tool.</summary>
public sealed record ToolRequestedSessionEvent(
    Guid PendingNodeId,
    string CallId,
    string PluginName,
    string FunctionName,
    string ArgumentsJson,
    bool IsAutoApproved) : SessionEvent;

/// <summary>A tool call completed (or was denied) — node updated in DB.</summary>
public sealed record ToolResultSessionEvent(Guid PendingNodeId, string ResultJson, ToolNodeStatus Status) : SessionEvent;

/// <summary>
/// The tool execution state changed:
/// <c>true</c> — at least one tool is currently executing; <c>false</c> — all completed.
/// </summary>
public sealed record ToolExecutionStateChangedSessionEvent(bool IsExecutingTools) : SessionEvent;

/// <summary>An error occurred during an LLM turn or tool call.</summary>
public sealed record SessionErrorEvent(string Message, Exception? Exception = null) : SessionEvent;

/// <summary>Context summarization started.</summary>
public sealed record SummarizationStartedSessionEvent(Guid ParentNodeId) : SessionEvent;

/// <summary>Context summarization completed.</summary>
public sealed record SummarizationCompletedSessionEvent(
    Guid ParentNodeId,
    Guid SummaryNodeId,
    string SummaryContent) : SessionEvent;

/// <summary>Session initialization event.</summary>
public sealed record InitializeSessionEvent() : SessionEvent;
