using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.UI.Models;

public enum SummarizationStatus
{
    Pending,    // Ожидает запуска
    Running,    // Выполняется
    Completed,  // Завершено
    Failed      // Ошибка
}

/// <summary>
/// Модель summary-узла — сжатого контекста предыдущего диалога.
/// Отображается компактной плашкой с индикатором статуса.
/// </summary>
public partial class SummarizationChatMessageModel : ChatMessageModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInProgress))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private SummarizationStatus _status = SummarizationStatus.Completed;

    [ObservableProperty]
    private string _summaryContent = string.Empty;

    public bool IsInProgress => Status is SummarizationStatus.Pending or SummarizationStatus.Running;

    public string StatusText => Status switch
    {
        SummarizationStatus.Pending   => "Ожидание...",
        SummarizationStatus.Running   => "Выполняется...",
        SummarizationStatus.Completed => "Готово",
        SummarizationStatus.Failed    => "Ошибка",
        _                             => Status.ToString()
    };

    public SummarizationChatMessageModel()
    {
        NodeType = MessageNodeType.Summary;
        CreatedAt = DateTime.UtcNow;
    }
}
