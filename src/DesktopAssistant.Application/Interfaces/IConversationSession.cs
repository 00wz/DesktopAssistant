using DesktopAssistant.Application.Dtos;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Encapsulates the state and logic of a single conversation:
/// LLM turns, tool calls, auto-approve, and conversation data.
/// Acts as a facade over <see cref="IChatService"/> — consumers (ChatViewModel, sub-agents)
/// do not depend on <see cref="IChatService"/> directly.
/// </summary>
public interface IConversationSession : IDisposable
{
    /// <summary>Conversation Id.</summary>
    Guid ConversationId { get; }

    /// <summary>Current conversation state.</summary>
    ConversationState State { get; }

    /// <summary>True while the session is automatically processing a turn (streaming / auto-approve).</summary>
    bool IsRunning { get; }

    /// <summary>True while at least one tool call is in the process of executing.</summary>
    bool IsExecutingTools { get; }

    /// <summary>
    /// Event published for all state and data changes.
    /// The handler is invoked on an arbitrary thread — marshalling to the UI thread is the subscriber's responsibility.
    /// </summary>
    event EventHandler<SessionEvent> EventOccurred;

    // ── Data facade (over IChatService) ───────────────────────────────────────

    /// <summary>Loads the history of the active branch.</summary>
    Task<IEnumerable<MessageDto>> LoadHistoryAsync(CancellationToken ct = default);

    /// <summary>Returns the conversation settings (system prompt, profile).</summary>
    Task<ConversationSettingsDto?> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>Saves the conversation's system prompt.</summary>
    Task UpdateSystemPromptAsync(string systemPrompt, CancellationToken ct = default);

    /// <summary>Changes the assistant profile for the conversation.</summary>
    Task ChangeProfileAsync(Guid profileId, CancellationToken ct = default);

    /// <summary>
    /// Summarizes the context starting from the selected node.
    /// Publishes <see cref="SummarizationSessionEvent"/> for each step of the process.
    /// </summary>
    Task SummarizeAsync(Guid selectedNodeId, CancellationToken ct = default);

    /// <summary>
    /// Switches to an alternative branch (sibling) and reinitializes the session.
    /// Publishes <see cref="InitializeSessionEvent"/> on completion.
    /// </summary>
    Task SwitchToSiblingAsync(Guid parentNodeId, Guid newChildId, CancellationToken ct = default);

    // ── LLM operations ────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a user message and starts an LLM turn.
    /// <para>
    /// If <paramref name="parentNodeId"/> is not specified — the message is appended to the current leaf (<see cref="ConversationSession.CurrentLeafNodeId"/>).
    /// If <paramref name="parentNodeId"/> differs from the current leaf — branching is performed:
    /// the session is reinitialized and publishes <see cref="InitializeSessionEvent"/>.
    /// </para>
    /// </summary>
    Task SendMessageAsync(string message, Guid? parentNodeId = null, CancellationToken ct = default);

    /// <summary>
    /// Resumes the conversation from the current leaf (e.g., after the application was interrupted).
    /// Only valid in state <see cref="ConversationState.LastMessageIsUser"/>
    /// or <see cref="ConversationState.AllToolCallsCompleted"/>.
    /// </summary>
    Task ResumeAsync(CancellationToken ct = default);

    /// <summary>
    /// Approves a pending tool call.
    /// After all tool calls of the current turn are complete, automatically starts the next LLM turn.
    /// </summary>
    Task ApproveToolAsync(Guid pendingNodeId, CancellationToken ct = default);

    /// <summary>
    /// Denies a pending tool call.
    /// After all tool calls of the current turn are complete, automatically starts the next LLM turn.
    /// </summary>
    Task DenyToolAsync(Guid pendingNodeId, CancellationToken ct = default);
}
