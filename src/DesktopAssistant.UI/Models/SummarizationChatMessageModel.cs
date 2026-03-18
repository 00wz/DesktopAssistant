using CommunityToolkit.Mvvm.ComponentModel;

namespace DesktopAssistant.UI.Models;

public enum SummarizationStatus
{
    Pending,    // Awaiting start
    Running,    // In progress
    Completed,  // Done
    Failed      // Error
}

/// <summary>
/// Model for a summary node — a condensed context of the previous conversation.
/// Displayed as a compact chip with a status indicator.
/// </summary>
public partial class SummarizationChatMessageModel : ChatMessageModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInProgress))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private SummarizationStatus _status = SummarizationStatus.Completed;

    [ObservableProperty]
    private string _summaryContent = string.Empty;

    public bool IsInProgress => Status is SummarizationStatus.Pending or SummarizationStatus.Running;

    public string StatusText => Status switch
    {
        SummarizationStatus.Pending   => "Pending...",
        SummarizationStatus.Running   => "Running...",
        SummarizationStatus.Completed => "Done",
        SummarizationStatus.Failed    => "Error",
        _                             => Status.ToString()
    };

    public SummarizationChatMessageModel()
    {
        CreatedAt = DateTime.UtcNow;
    }
}
