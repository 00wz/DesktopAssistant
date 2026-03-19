namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Result of executing or denying a tool call.
/// Returned from ApproveToolCallAsync / DenyToolCallAsync.
/// </summary>
public record ToolCallResult(string ResultJson, ToolNodeStatus Status);
