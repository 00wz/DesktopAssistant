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
/// Abstract base for all tool-call message models.
/// Holds properties common to both a regular tool call and an agent result:
/// identifiers, status, and the final result JSON.
/// <para>
/// Concrete subtypes:
/// <list type="bullet">
///   <item><see cref="RegularToolCallModel"/> — a tool call that can be approved/denied.</item>
///   <item><see cref="AgentResultModel"/> — the terminal output node of an agent sub-task.</item>
/// </list>
/// </para>
/// </summary>
public abstract partial class ToolChatMessageModel : ChatMessageModel
{
    [ObservableProperty]
    private string _callId = string.Empty;

    [ObservableProperty]
    private string _pluginName = string.Empty;

    [ObservableProperty]
    private string _functionName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string _resultJson = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsExecuting))]
    private ToolCallStatus _status = ToolCallStatus.Pending;

    public bool HasResult   => !string.IsNullOrEmpty(ResultJson);
    public bool IsPending   => Status == ToolCallStatus.Pending;
    public bool IsExecuting => Status == ToolCallStatus.Executing;
    public bool IsCompleted => Status == ToolCallStatus.Completed;
    public bool IsFailed    => Status is ToolCallStatus.Failed or ToolCallStatus.Denied;

    protected ToolChatMessageModel()
    {
        CreatedAt = DateTime.UtcNow;
    }
}
