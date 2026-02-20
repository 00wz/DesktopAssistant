using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// Модель текстового сообщения пользователя или ассистента.
/// Поддерживает потоковое добавление текста и редактирование.
/// </summary>
public partial class TextChatMessageModel : ChatMessageModel
{
    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editedContent = string.Empty;

    public bool IsUser => NodeType == MessageNodeType.User;
    public bool IsAssistant => NodeType == MessageNodeType.Assistant;

    public TextChatMessageModel() { }

    public TextChatMessageModel(Guid id, MessageNodeType nodeType, string content, DateTime createdAt)
    {
        Id = id;
        NodeType = nodeType;
        Content = content;
        CreatedAt = createdAt;
    }

    /// <summary>Добавляет чанк стримингового ответа к контенту.</summary>
    public void AppendContent(string chunk) => Content += chunk;
}
