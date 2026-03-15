using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// User message model. Supports editing.
/// </summary>
public partial class UserChatMessageModel : ChatMessageModel
{
    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editedContent = string.Empty;

    public UserChatMessageModel()
    {
        NodeType = MessageNodeType.User;
    }

    public UserChatMessageModel(Guid id, string content, DateTime createdAt)
    {
        Id = id;
        NodeType = MessageNodeType.User;
        Content = content;
        CreatedAt = createdAt;
    }
}
