using DesktopAssistant.Application.Dtos;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Manages the lifecycle of sub-agent conversations.
/// Maintains an in-memory registry of pending sub-agents and resolves them when they call complete_task.
/// </summary>
public interface ISubagentService
{
    /// <summary>
    /// Creates (or resumes) a sub-agent conversation for the given tool node and waits for it to finish.
    /// If the sub-agent for <paramref name="toolNodeId"/> already exists and has completed,
    /// returns the cached result immediately. Otherwise awaits the next complete_task call.
    /// </summary>
    Task<string> RunSubagentAsync(
        Guid parentConversationId,
        Guid toolNodeId,
        string firstMessage,
        string? systemPrompt,
        bool canSpawnSubagents,
        string? name,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a new message to an existing sub-agent and waits for it to finish.
    /// Only valid when the sub-agent is in state LastMessageIsAssistant or AgentTaskCompleted.
    /// </summary>
    Task<string> SendMessageToSubagentAsync(
        Guid conversationId,
        string message,
        CancellationToken ct = default);

    /// <summary>Returns all direct sub-agents of the specified parent conversation.</summary>
    Task<IReadOnlyList<SubagentInfoDto>> GetSubagentsAsync(
        Guid parentConversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Called by AgentOutputPlugin.complete_task — resolves the pending awaiter for the conversation.
    /// No-op if no awaiter is registered (e.g., top-level agent call).
    /// </summary>
    void CompleteSubagent(Guid conversationId, string result);
}
