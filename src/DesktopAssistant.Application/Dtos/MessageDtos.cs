namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Base DTO for conversation messages. Replaces direct use of the domain entity MessageNode in the UI.
/// </summary>
public abstract record MessageDto(Guid Id, Guid? ParentId, DateTime CreatedAt);

/// <summary>User message. Includes sibling information for branch navigation.</summary>
public record UserMessageDto(
    Guid Id,
    Guid? ParentId,
    DateTime CreatedAt,
    string Content,
    int CurrentSiblingIndex = 1,
    int TotalSiblings = 1,
    bool HasPreviousSibling = false,
    bool HasNextSibling = false,
    Guid? PreviousSiblingId = null,
    Guid? NextSiblingId = null
) : MessageDto(Id, ParentId, CreatedAt);

/// <summary>Assistant message. Includes sibling information.</summary>
public record AssistantMessageDto(
    Guid Id,
    Guid? ParentId,
    DateTime CreatedAt,
    string Content,
    int CurrentSiblingIndex = 1,
    int TotalSiblings = 1,
    bool HasPreviousSibling = false,
    bool HasNextSibling = false,
    Guid? PreviousSiblingId = null,
    Guid? NextSiblingId = null,
    int InputTokenCount = 0,
    int OutputTokenCount = 0,
    int TotalTokenCount = 0
) : MessageDto(Id, ParentId, CreatedAt);

/// <summary>
/// Result of a tool call execution.
/// Status == Pending: the node is saved in the database, awaiting user confirmation.
/// </summary>
public record ToolResultDto(
    Guid Id,
    Guid? ParentId,
    DateTime CreatedAt,
    string CallId,
    string PluginName,
    string FunctionName,
    string ResultJson,
    ToolNodeStatus Status,
    string ArgumentsJson = "",
    bool IsTerminal = false
) : MessageDto(Id, ParentId, CreatedAt);

/// <summary>Summary node — a condensed context of the previous conversation.</summary>
public record SummaryMessageDto(
    Guid Id,
    Guid? ParentId,
    DateTime CreatedAt,
    string SummaryContent
) : MessageDto(Id, ParentId, CreatedAt);

/// <summary>Conversation DTO — returned from IChatService instead of the domain entity Conversation.</summary>
public record ConversationDto(
    Guid Id,
    string Title,
    Guid? ActiveLeafNodeId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid? AssistantProfileId = null,
    bool CanSpawnSubagents = false);

/// <summary>Minimal info about a sub-agent conversation.</summary>
public record SubagentInfoDto(Guid Id, string Title);
