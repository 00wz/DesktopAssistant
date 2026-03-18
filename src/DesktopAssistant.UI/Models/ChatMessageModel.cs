using CommunityToolkit.Mvvm.ComponentModel;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// Abstract base message model for display in the UI.
/// Contains common properties: identifier, type, timestamp, parent, and sibling navigation.
/// Concrete subtypes: UserChatMessageModel, AssistantChatMessageModel, ToolChatMessageModel, SummarizationChatMessageModel.
/// </summary>
public abstract partial class ChatMessageModel : ObservableObject
{
    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private DateTime _createdAt;

    // Not observable — used for branch navigation logic
    public Guid? ParentId { get; set; }
    public Guid? PreviousSiblingId { get; set; }
    public Guid? NextSiblingId { get; set; }

    // Sibling navigation (1-based index for display)
    [ObservableProperty]
    private bool _hasPreviousSibling;

    [ObservableProperty]
    private bool _hasNextSibling;

    [ObservableProperty]
    private int _currentSiblingIndex;

    [ObservableProperty]
    private int _totalSiblings;
}
