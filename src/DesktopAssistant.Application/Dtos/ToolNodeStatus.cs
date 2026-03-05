namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Персистируемый статус tool-узла. Хранится в ToolNodeMetadata и передаётся через ToolResultDto.
/// Не включает Executing — это runtime-only состояние UI.
/// </summary>
public enum ToolNodeStatus
{
    Pending,    // ResultJson == null — ожидает подтверждения
    Completed,  // Успешно выполнен
    Failed,     // Завершился ошибкой
    Denied      // Отклонён пользователем
}
