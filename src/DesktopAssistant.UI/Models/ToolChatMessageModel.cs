using CommunityToolkit.Mvvm.ComponentModel;

namespace DesktopAssistant.UI.Models;

public enum ToolCallStatus
{
    Pending,    // Awaiting user confirmation
    Executing,  // Running (ApproveToolCallAsync in progress)
    Completed,  // Successfully completed
    Failed,     // Finished with an error
    Denied      // Rejected by the user
}

/// <summary>
/// Model for a tool call. Displays the tool name, arguments, result, and status.
/// When Status == Pending, waits for the user to press Approve/Deny.
/// Id == PendingNodeId from the DB — passed to ApproveToolCallAsync / DenyToolCallAsync.
/// </summary>
public partial class ToolChatMessageModel : ChatMessageModel
{
    [ObservableProperty]
    private string _callId = string.Empty;

    [ObservableProperty]
    private string _pluginName = string.Empty;

    [ObservableProperty]
    private string _functionName = string.Empty;

    [ObservableProperty]
    private string _argumentsJson = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string _resultJson = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsExecuting))]
    private ToolCallStatus _status = ToolCallStatus.Pending;

    [ObservableProperty]
    private bool _isTerminal;

    public bool HasResult => !string.IsNullOrEmpty(ResultJson);
    public bool IsPending => Status == ToolCallStatus.Pending;
    public bool IsExecuting => Status == ToolCallStatus.Executing;
    public bool IsCompleted => Status == ToolCallStatus.Completed;
    public bool IsFailed => Status is ToolCallStatus.Failed or ToolCallStatus.Denied;

    public ToolChatMessageModel()
    {
        CreatedAt = DateTime.UtcNow;
    }
}
