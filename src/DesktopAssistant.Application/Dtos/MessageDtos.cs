namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Базовый DTO для сообщений диалога. Заменяет прямое использование доменной сущности MessageNode в UI.
/// </summary>
public abstract record MessageDto(Guid Id, Guid? ParentId, DateTime CreatedAt);

/// <summary>Сообщение пользователя. Включает информацию о siblings для навигации по ветвям.</summary>
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

/// <summary>Сообщение ассистента. Включает информацию о siblings.</summary>
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
/// Результат выполнения tool-вызова.
/// IsPending == true: узел сохранён в БД как __PENDING_TOOL__, ожидает подтверждения пользователя.
/// </summary>
public record ToolResultDto(
    Guid Id,
    Guid? ParentId,
    DateTime CreatedAt,
    string CallId,
    string PluginName,
    string FunctionName,
    string ResultJson,
    bool IsPending = false,
    string ArgumentsJson = ""
) : MessageDto(Id, ParentId, CreatedAt);

/// <summary>Сводный узел — сжатый контекст предыдущего диалога.</summary>
public record SummaryMessageDto(
    Guid Id,
    Guid? ParentId,
    DateTime CreatedAt,
    string SummaryContent,
    int InputTokenCount,
    int OutputTokenCount
) : MessageDto(Id, ParentId, CreatedAt);

/// <summary>DTO диалога — возвращается из IChatService вместо Domain-сущности Conversation.</summary>
public record ConversationDto(
    Guid Id,
    string Title,
    Guid? ActiveLeafNodeId,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
