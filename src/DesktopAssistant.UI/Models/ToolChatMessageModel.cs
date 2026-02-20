using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.UI.Models;

public enum ToolCallStatus
{
    Pending,    // Ожидает подтверждения пользователя
    Approved,   // Подтверждён, ещё не выполняется
    Executing,  // Выполняется
    Completed,  // Успешно завершён
    Failed,     // Завершился ошибкой
    Denied      // Отклонён пользователем
}

/// <summary>
/// Модель tool-вызова. Отображает имя инструмента, аргументы, результат и статус.
/// При Status == Pending ожидает вызова Approve() или Deny() для разрешения TCS.
/// </summary>
public partial class ToolChatMessageModel : ChatMessageModel
{
    [ObservableProperty]
    private string _callId = string.Empty;

    [ObservableProperty]
    private string _pluginName = string.Empty;

    [ObservableProperty]
    private string _functionName = string.Empty;

    [ObservableProperty]
    private string _argumentsJson = string.Empty;

    [ObservableProperty]
    private string _resultJson = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsExecuting))]
    private ToolCallStatus _status = ToolCallStatus.Pending;

    public bool IsPending => Status == ToolCallStatus.Pending;
    public bool IsExecuting => Status is ToolCallStatus.Approved or ToolCallStatus.Executing;
    public bool IsCompleted => Status == ToolCallStatus.Completed;
    public bool IsFailed => Status is ToolCallStatus.Failed or ToolCallStatus.Denied;

    /// <summary>
    /// TCS для подтверждения/отклонения tool-вызова.
    /// Не участвует в биндинге UI — управляется через команды ViewModel.
    /// </summary>
    public TaskCompletionSource<bool>? Confirmation { get; set; }

    public ToolChatMessageModel()
    {
        NodeType = MessageNodeType.Tool;
        CreatedAt = DateTime.UtcNow;
    }
}
