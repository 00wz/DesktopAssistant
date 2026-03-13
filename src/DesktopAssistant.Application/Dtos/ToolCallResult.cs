namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Результат выполнения или отклонения tool-вызова.
/// Возвращается из ApproveToolCallAsync / DenyToolCallAsync.
/// </summary>
public record ToolCallResult(string ResultJson, ToolNodeStatus Status, Guid AssistantNodeId = default);
