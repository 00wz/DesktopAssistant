using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// Абстрактная базовая модель сообщения для отображения в UI.
/// Содержит общие свойства: идентификатор, тип, временная метка, родитель, навигация по siblings.
/// Конкретные подтипы: TextChatMessageModel, ToolChatMessageModel, SummarizationChatMessageModel.
/// </summary>
public abstract partial class ChatMessageModel : ObservableObject
{
    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private MessageNodeType _nodeType;

    [ObservableProperty]
    private DateTime _createdAt;

    // Не observable — используется для логики навигации по ветвям
    public Guid? ParentId { get; set; }

    // Навигация по siblings (1-based индекс для отображения)
    [ObservableProperty]
    private bool _hasPreviousSibling;

    [ObservableProperty]
    private bool _hasNextSibling;

    [ObservableProperty]
    private int _currentSiblingIndex;

    [ObservableProperty]
    private int _totalSiblings;
}
