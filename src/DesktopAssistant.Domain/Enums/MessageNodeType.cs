namespace DesktopAssistant.Domain.Enums;

/// <summary>
/// Type of a message node in the conversation graph
/// </summary>
public enum MessageNodeType
{
    /// <summary>
    /// System prompt
    /// </summary>
    Root = 0,

    /// <summary>
    /// User message
    /// </summary>
    User = 1,

    /// <summary>
    /// Assistant response
    /// </summary>
    Assistant = 2,

    /// <summary>
    /// Summarization node - contains a summary of previous context.
    /// When building context for the LLM, the algorithm walks back along the branch
    /// and stops at this node, using its content as a "starting point"
    /// instead of the full previous history.
    /// </summary>
    Summary = 3,

    /// <summary>
    /// Function call result (tool result).
    /// Used to store FunctionResultContent in manual tool invocation mode.
    /// </summary>
    Tool = 4
}
