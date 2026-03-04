namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Состояние диалога с точки зрения UI — определяет доступные действия пользователя.
/// </summary>
public enum ConversationState
{
    /// <summary>Последнее сообщение — от ассистента без tool-вызовов. Пользователь может писать.</summary>
    LastMessageIsAssistant = 0,

    /// <summary>Последнее сообщение — от пользователя (LLM не ответил). Показать кнопку «Возобновить».</summary>
    LastMessageIsUser,

    /// <summary>Все tool-вызовы выполнены, но LLM ещё не получил результаты. Показать кнопку «Возобновить».</summary>
    AllToolCallsCompleted,

    /// <summary>Есть ожидающие tool-вызовы. Пользователь ждёт подтверждения/отклонения.</summary>
    HasPendingToolCalls,

    /// <summary>
    /// Идентификаторы tool-результатов не совпадают с идентификаторами tool-вызовов ассистента.
    /// Например: узел с результатом не был создан, или был передан id не последнего результата.
    /// </summary>
    ToolCallIdMismatch,
}
