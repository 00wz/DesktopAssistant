namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Базовый тип событий, которые <c>IConversationSession</c> публикует подписчикам.
/// </summary>
public abstract record SessionEvent;

/// <summary>Сессия начала или закончила автоматическую обработку (стриминг / auto-approve tools).</summary>
public sealed record RunningStateChangedSessionEvent(bool IsRunning) : SessionEvent;

/// <summary>Состояние диалога изменилось (например, после завершения всех tool-вызовов).</summary>
public sealed record ConversationStateChangedSessionEvent(ConversationState State) : SessionEvent;

/// <summary>Пользователь отправил сообщение — узел сохранён в БД.</summary>
public sealed record UserMessageAddedSessionEvent(UserMessageDto Dto) : SessionEvent;

/// <summary>LLM начал новый тёрн ответа.</summary>
public sealed record AssistantTurnStartedSessionEvent(Guid TempId, DateTime StartedAt) : SessionEvent;

/// <summary>Текстовый чанк текущего тёрна ассистента.</summary>
public sealed record AssistantChunkSessionEvent(string Text) : SessionEvent;

/// <summary>Тёрн ассистента завершён и сохранён в БД.</summary>
public sealed record AssistantResponseSavedSessionEvent(Guid LastNodeId, int TotalTokenCount) : SessionEvent;

/// <summary>LLM вызывает tool.</summary>
public sealed record ToolRequestedSessionEvent(
    Guid PendingNodeId,
    string CallId,
    string PluginName,
    string FunctionName,
    string ArgumentsJson,
    bool IsAutoApproved) : SessionEvent;

/// TODO: возможно, добавить ToolApprovedSessionEvent

/// <summary>Tool-вызов выполнен (или отклонён) — узел обновлён в БД.</summary>
public sealed record ToolResultSessionEvent(Guid PendingNodeId, string ResultJson, ToolNodeStatus Status) : SessionEvent;

/// <summary>Произошла ошибка во время выполнения LLM-тёрна или tool-вызова.</summary>
public sealed record SessionErrorEvent(string Message, Exception? Exception = null) : SessionEvent;

/// <summary>Суммаризация контекста начата.</summary>
public sealed record SummarizationStartedSessionEvent(Guid ParentNodeId) : SessionEvent;

/// <summary>Суммаризация контекста завершена.</summary>
public sealed record SummarizationCompletedSessionEvent(
    Guid ParentNodeId,
    Guid SummaryNodeId,
    string SummaryContent) : SessionEvent;

/// <summary>Событие инициализации сессии.</summary>
public sealed record InitializeSessionEvent() : SessionEvent;
