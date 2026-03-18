namespace DesktopAssistant.Domain.Enums;

/// <summary>
/// Controls how the LLM interacts with tools during a conversation.
/// </summary>
public enum ConversationMode
{
    /// <summary>
    /// Standard chat mode. Tool use is optional; the LLM decides when to call tools.
    /// </summary>
    Chat = 0,

    /// <summary>
    /// Agent mode. The LLM must call a tool on every turn.
    /// The loop ends when the LLM calls a terminal tool from AgentControlPlugin
    /// (FinishTask or FailTask), which signals that the task is complete.
    /// </summary>
    Agent = 1,
}
