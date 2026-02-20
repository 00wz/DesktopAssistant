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
    private SummarizationStatus _status = SummarizationStatus.Completed;

    [ObservableProperty]
    private string _summaryContent = string.Empty;

    [ObservableProperty]
    private int _inputTokenCount;

    [ObservableProperty]
    private int _outputTokenCount;

    public bool IsInProgress => Status is SummarizationStatus.Pending or SummarizationStatus.Running;

    public SummarizationChatMessageModel()
    {
        NodeType = MessageNodeType.Summary;
        CreatedAt = DateTime.UtcNow;
    }
}
