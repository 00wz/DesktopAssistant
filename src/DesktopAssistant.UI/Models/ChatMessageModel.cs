using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// Модель сообщения для отображения в UI
/// </summary>
public partial class ChatMessageModel : ObservableObject
{
    [ObservableProperty]
    private Guid _id;
    
    [ObservableProperty]
    private MessageNodeType _nodeType;
    
    [ObservableProperty]
    private string _content = string.Empty;
    
    [ObservableProperty]
    private DateTime _createdAt;
    
    [ObservableProperty]
    private bool _isStreaming;

    // Editing mode
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editedContent = string.Empty;

    // Sibling navigation
    [ObservableProperty]
    private bool _hasPreviousSibling;

    [ObservableProperty]
    private bool _hasNextSibling;

    [ObservableProperty]
    private int _currentSiblingIndex; // 1-based for display

    [ObservableProperty]
    private int _totalSiblings;

    // ParentId for branching logic
    public Guid? ParentId { get; set; }

    public bool IsUser => NodeType == MessageNodeType.User;
    public bool IsAssistant => NodeType == MessageNodeType.Assistant;
    public bool IsSystem => NodeType == MessageNodeType.System;

    public ChatMessageModel() { }

    public ChatMessageModel(Guid id, MessageNodeType nodeType, string content, DateTime createdAt)
    {
        Id = id;
        NodeType = nodeType;
        Content = content;
        CreatedAt = createdAt;
    }

    public void AppendContent(string chunk)
    {
        Content += chunk;
    }
}
