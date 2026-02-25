namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Результат выполнения или отклонения tool-вызова.
/// Возвращается из ApproveToolCallAsync / DenyToolCallAsync.
/// </summary>
public record ToolCallResult(
    bool IsError,
    string ResultJson,
    string? ErrorMessage,
    bool AllToolsForTurnCompleted);
