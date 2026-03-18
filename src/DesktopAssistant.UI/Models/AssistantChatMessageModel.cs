using CommunityToolkit.Mvvm.ComponentModel;
using LiveMarkdown.Avalonia;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// Assistant message model. Supports streaming text append.
/// </summary>
public partial class AssistantChatMessageModel : ChatMessageModel
{
    public ObservableStringBuilder MarkdownBuilder { get; } = new();

    private bool _hasContent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _isStreaming;

    public bool IsVisible => _hasContent || IsStreaming;

    public AssistantChatMessageModel() { }

    public AssistantChatMessageModel(Guid id, string content, DateTime createdAt)
    {
        Id = id;
        _hasContent = !string.IsNullOrEmpty(content);
        MarkdownBuilder.Append(content);
        CreatedAt = createdAt;
    }

    /// <summary>Appends a streaming response chunk to the content.</summary>
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
