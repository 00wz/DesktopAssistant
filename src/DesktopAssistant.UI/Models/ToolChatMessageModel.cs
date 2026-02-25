using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.UI.Models;

public enum ToolCallStatus
{
    Pending,    // Ожидает подтверждения пользователя
    Executing,  // Выполняется (ApproveToolCallAsync в процессе)
    Completed,  // Успешно завершён
    Failed,     // Завершился ошибкой
    Denied      // Отклонён пользователем
}

/// <summary>
/// Модель tool-вызова. Отображает имя инструмента, аргументы, результат и статус.
/// При Status == Pending ожидает нажатия кнопок Approve/Deny.
/// Id == PendingNodeId из БД — передаётся в ApproveToolCallAsync / DenyToolCallAsync.
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
    public bool IsExecuting => Status == ToolCallStatus.Executing;
    public bool IsCompleted => Status == ToolCallStatus.Completed;
    public bool IsFailed => Status is ToolCallStatus.Failed or ToolCallStatus.Denied;

    public ToolChatMessageModel()
    {
        NodeType = MessageNodeType.Tool;
        CreatedAt = DateTime.UtcNow;
    }
}
