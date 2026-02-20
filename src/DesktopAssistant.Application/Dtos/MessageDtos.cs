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
    bool HasNextSibling = false
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
    bool HasNextSibling = false
) : MessageDto(Id, ParentId, CreatedAt);

/// <summary>Результат выполнения tool-вызова.</summary>
public record ToolResultDto(
    Guid Id,
    Guid? ParentId,
    DateTime CreatedAt,
    string CallId,
    string PluginName,
    string FunctionName,
    string ResultJson
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
