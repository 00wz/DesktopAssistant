namespace DesktopAssistant.UI.Models;

/// <summary>
/// An item in the saved conversations list.
/// </summary>
public class ConversationListItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Id of the parent conversation. Null for root-level conversations.
    /// Used by <see cref="SidebarViewModel"/> to build the tree.
    /// </summary>
    public Guid? ParentId { get; set; }

}
