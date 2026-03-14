using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// Модель сообщения ассистента. Поддерживает потоковое добавление текста.
/// </summary>
public partial class AssistantChatMessageModel : ChatMessageModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private string _content = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _isStreaming;

    public bool IsVisible => !string.IsNullOrEmpty(Content) || IsStreaming;

    public AssistantChatMessageModel()
    {
        NodeType = MessageNodeType.Assistant;
    }

    public AssistantChatMessageModel(Guid id, string content, DateTime createdAt)
    {
        Id = id;
        NodeType = MessageNodeType.Assistant;
        Content = content;
        CreatedAt = createdAt;
    }

    /// <summary>Добавляет чанк стримингового ответа к контенту.</summary>
    public void AppendContent(string chunk) => Content += chunk;
}
