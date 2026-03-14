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

    private bool _hasContent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _isStreaming;

    public bool IsVisible => _hasContent || IsStreaming;

    public AssistantChatMessageModel()
    {
        NodeType = MessageNodeType.Assistant;
    }

    public AssistantChatMessageModel(Guid id, string content, DateTime createdAt)
    {
        Id = id;
        NodeType = MessageNodeType.Assistant;
        _hasContent = !string.IsNullOrEmpty(content);
        MarkdownBuilder.Append(content);
        CreatedAt = createdAt;
    }

    /// <summary>Добавляет чанк стримингового ответа к контенту.</summary>
    public void AppendContent(string chunk)
    {
        MarkdownBuilder.Append(chunk);
        if (!_hasContent)
        {
            _hasContent = true;
            OnPropertyChanged(nameof(IsVisible));
        }
    }
}
