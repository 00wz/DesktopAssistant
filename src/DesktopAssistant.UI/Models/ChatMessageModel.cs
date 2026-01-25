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
