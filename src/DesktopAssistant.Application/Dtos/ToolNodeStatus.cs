namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Persistable status of a tool node. Stored in ToolNodeMetadata and passed through ToolResultDto.
/// Does not include Executing — that is a runtime-only UI state.
/// </summary>
public enum ToolNodeStatus
{
    Pending,    // ResultJson == null — awaiting confirmation
    Completed,  // Successfully executed
    Failed,     // Finished with an error
    Denied      // Denied by the user
}
