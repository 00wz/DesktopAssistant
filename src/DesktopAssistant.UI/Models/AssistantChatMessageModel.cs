using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// Модель сообщения ассистента. Поддерживает потоковое добавление текста.
/// </summary>
public partial class AssistantChatMessageModel : ChatMessageModel
{
    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private bool _isStreaming;

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
