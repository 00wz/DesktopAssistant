using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Domain.Enums;
using LiveMarkdown.Avalonia;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// Модель сообщения ассистента. Поддерживает потоковое добавление текста.
/// </summary>
public partial class AssistantChatMessageModel : ChatMessageModel
{
    public ObservableStringBuilder MarkdownBuilder { get; } = new();

    private string _content = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _isStreaming;

    public bool IsVisible => !string.IsNullOrEmpty(_content) || IsStreaming;

    public AssistantChatMessageModel()
    {
        NodeType = MessageNodeType.Assistant;
    }

    public AssistantChatMessageModel(Guid id, string content, DateTime createdAt)
    {
        Id = id;
        NodeType = MessageNodeType.Assistant;
        _content = content;
        MarkdownBuilder.Append(content);
        CreatedAt = createdAt;
    }

    /// <summary>Добавляет чанк стримингового ответа к контенту.</summary>
    public void AppendContent(string chunk)
    {
        _content += chunk;
        MarkdownBuilder.Append(chunk);
        OnPropertyChanged(nameof(IsVisible));
    }
}
