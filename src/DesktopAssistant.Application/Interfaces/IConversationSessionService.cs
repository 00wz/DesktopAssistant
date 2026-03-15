namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Singleton pool of conversation sessions.
/// Guarantees that exactly one active session exists per conversation.
/// </summary>
public interface IConversationSessionService : IDisposable
{
    /// <summary>
    /// A snapshot of the identifiers of all active sessions at the time of the call.
    /// </summary>
    IReadOnlyCollection<Guid> ActiveSessionIds { get; }

    /// <summary>
    /// Raised after a new session is added to the pool.
    /// The argument is the conversation identifier.
    /// </summary>
    event EventHandler<Guid> SessionCreated;

    /// <summary>
    /// Raised after a session is removed from the pool.
    /// The argument is the conversation identifier.
    /// </summary>
    event EventHandler<Guid> SessionReleased;

    /// <summary>
    /// Returns an existing session or creates and initializes a new one.
    /// </summary>
    Task<IConversationSession> GetOrCreate(Guid conversationId);

    /// <summary>
    /// Releases a conversation session and removes it from the pool.
    /// Called when a conversation is deleted.
    /// </summary>
    void Release(Guid conversationId);
}
