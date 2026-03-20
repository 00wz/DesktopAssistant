using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Service for interacting with an LLM via chat: conversation management, message history, LLM and tool calls.
/// </summary>
public interface IChatService
{
    // ── Conversation management ──────────────────────────────────────────────

    /// <summary>
    /// Creates a new conversation.
    /// If assistantProfileId is not specified — the default profile is used.
    /// </summary>
    Task<ConversationDto> CreateConversationAsync(
        string title,
        Guid? assistantProfileId = null,
        string systemPrompt = "",
        ConversationMode mode = ConversationMode.Chat,
        bool canSpawnSubagents = false,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all active conversations.</summary>
    Task<IEnumerable<ConversationDto>> GetConversationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a conversation by ID. Returns null if not found.</summary>
    Task<ConversationDto?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Returns the conversation settings (system prompt, profile).</summary>
    Task<ConversationSettingsDto?> GetConversationSettingsAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Updates the conversation's system prompt.</summary>
    Task UpdateConversationSystemPromptAsync(Guid conversationId, string systemPrompt, CancellationToken cancellationToken = default);

    /// <summary>Changes the assistant profile for the conversation.</summary>
    Task ChangeConversationProfileAsync(Guid conversationId, Guid newProfileId, CancellationToken cancellationToken = default);

    /// <summary>Changes the conversation mode (Chat / Agent).</summary>
    Task ChangeConversationModeAsync(Guid conversationId, ConversationMode mode, CancellationToken cancellationToken = default);

    /// <summary>Controls whether the LLM in this conversation can spawn sub-agents.</summary>
    Task ChangeCanSpawnSubagentsAsync(Guid conversationId, bool canSpawnSubagents, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the tool node that currently owns this conversation.
    /// Used by send_message_to_subagent so retries of the same tool call can be detected and recovered.
    /// </summary>
    Task UpdateSpawnedByToolNodeAsync(Guid conversationId, Guid toolNodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the message history of the current active branch of a conversation as DTOs.
    /// Includes sibling information for each node.
    /// System messages are not included.
    /// </summary>
    Task<IEnumerable<MessageDto>> GetConversationHistoryAsync(Guid conversationId, Guid lastNodeId, CancellationToken cancellationToken = default);

    /// <summary>Switches to an alternative branch (sibling) of a message.</summary>
    Task SwitchToSiblingAsync(Guid conversationId, Guid parentNodeId, Guid newChildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a user message as a child node of the specified parent (to create a sibling).
    /// Returns a DTO with sibling information.
    /// </summary>
    Task<UserMessageDto> AddUserMessageAsync(Guid conversationId, Guid parentNodeId, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds context starting from the specified message and submits a request to the assistant.
    /// Returns an IAsyncEnumerable stream of events:
    /// AssistantTurnDto → (ChunkReceived events) → ToolCallRequestedDto →
    /// AssistantResponseSavedDto → ...
    /// </summary>
    IAsyncEnumerable<StreamEvent> GetAssistantResponseAsync(Guid conversationId, Guid lastMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a pending tool call (pendingNodeId — ID of the node with ToolNodeMetadata.ResultJson == null).
    /// Stateless: all data is restored from the database by pendingNodeId.
    /// </summary>
    Task<ToolCallResult> ApproveToolCallAsync(Guid pendingNodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Denies a pending tool call.
    /// Updates the node with status "Denied by user" and returns a ToolCallResult.
    /// </summary>
    Task<ToolCallResult> DenyToolCallAsync(Guid pendingNodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines the current state of the conversation, starting traversal from lastNodeId.
    /// Returns <see cref="ConversationState"/> describing the available user actions.
    /// </summary>
    Task<ConversationState> GetConversationStateAsync(Guid lastNodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Summarizes the conversation context starting from the selected node.
    /// Creates a summary node in the tree as a child of selectedNodeId.
    /// Event stream: SummarizationStartedDto → SummarizationCompletedDto.
    /// </summary>
    IAsyncEnumerable<SummarizationEvent> SummarizeAsync(
        Guid conversationId,
        Guid selectedNodeId,
        CancellationToken cancellationToken = default);

    // ── Sub-agent management ─────────────────────────────────────────────────

    /// <summary>
    /// Creates a sub-agent conversation linked to a parent via a tool node.
    /// The sub-agent always runs in Agent mode and inherits the parent's profile.
    /// </summary>
    Task<ConversationDto> CreateSubagentConversationAsync(
        Guid parentConversationId,
        Guid spawnedByToolNodeId,
        string title,
        Guid profileId,
        string systemPrompt,
        bool canSpawnSubagents,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a conversation by the tool node that spawned it. Returns null if not found.</summary>
    Task<ConversationDto?> GetConversationByToolNodeIdAsync(
        Guid toolNodeId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all direct sub-agent conversations of a parent.</summary>
    Task<IReadOnlyList<SubagentInfoDto>> GetSubagentConversationsAsync(
        Guid parentConversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the result message from the last successful complete_task call,
    /// traversing backwards from <paramref name="lastNodeId"/>.
    /// Returns null if no completed terminal tool is found.
    /// </summary>
    Task<string?> GetLastCompleteTaskResultAsync(
        Guid lastNodeId,
        CancellationToken cancellationToken = default);
}
