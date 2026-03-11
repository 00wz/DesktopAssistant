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

//TODO: возможно, стоит добавить SessionEvent для каждого StreamEvent, что бы сделать клиентский код независимым от StreamEvent
/// <summary>Событие из LLM-стрима — оборачивает <see cref="StreamEvent"/>.</summary>
public sealed record StreamSessionEvent(StreamEvent Inner) : SessionEvent;

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

//TODO: возможно разделить SummarizationSessionEvent, что бы сделать клиентский код независимым от SummarizationEvent
/// <summary>Событие суммаризации контекста.</summary>
public sealed record SummarizationSessionEvent(SummarizationEvent evt) : SessionEvent;

/// <summary>Событие инициализации сессии.</summary>
public sealed record InitializeSessionEvent() : SessionEvent;
