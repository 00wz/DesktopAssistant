namespace DesktopAssistant.UI.Models;

/// <summary>
/// Status icon for an active conversation session in the sidebar.
/// </summary>
public enum SessionStatusIcon
{
    /// <summary>Icon is not displayed.</summary>
    None,

    /// <summary>Animated loading icon — the session is executing an LLM turn.</summary>
    Loading,

    /// <summary>Pause icon — waiting for user input or resumption.</summary>
    Paused,

    /// <summary>Animated question icon — waiting for tool-call approval.</summary>
    Question,

    /// <summary>Error icon — tool-call ID mismatch.</summary>
    Error,
}
