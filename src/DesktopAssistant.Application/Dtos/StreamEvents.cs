namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Базовый тип для элементов IAsyncEnumerable-потока от LLM.
/// Каждый элемент потока GetAssistantResponseAsync — один из подтипов.
/// </summary>
public abstract record StreamEvent;

/// <summary>
/// Начало нового тёрна ассистента. Один объект на весь тёрн — не на чанк.
/// ChunkReceived вызывается ChatService из фонового потока по мере поступления чанков.
/// Completed вызывается перед yield следующего элемента потока.
/// Consumer обязан подписаться на события до вызова MoveNextAsync (гарантируется await foreach).
/// </summary>
public sealed record AssistantTurnDto : StreamEvent
{
    public Guid TempId { get; } = Guid.NewGuid();
    public DateTime StartedAt { get; } = DateTime.UtcNow;

    /// <summary>Вызывается из фонового потока ChatService при получении каждого текстового чанка.</summary>
    public event Action<string>? ChunkReceived;

    /// <summary>
    /// Вызывается из фонового потока ChatService после последнего чанка,
    /// непосредственно перед yield следующего StreamEvent.
    /// </summary>
    public event Action? Completed;

    public void OnChunk(string chunk) => ChunkReceived?.Invoke(chunk);
    public void OnCompleted() => Completed?.Invoke();
}

/// <summary>Tool-вызов, требующий подтверждения пользователя.
/// Producer awaits Confirmation.Task после yield — producer заблокирован до разрешения TCS.
/// Consumer должен вызвать Confirmation.TrySetResult(true/false) через действие пользователя.</summary>
public sealed record ToolCallRequestedDto(
    string CallId,
    string PluginName,
    string FunctionName,
    string ArgumentsJson,
    TaskCompletionSource<bool> Confirmation) : StreamEvent;

/// <summary>Tool начал выполнение (подтверждение получено, InvokeAsync вызван).</summary>
public sealed record ToolCallExecutingDto(string CallId) : StreamEvent;

/// <summary>Tool успешно завершён.</summary>
public sealed record ToolCallCompletedDto(string CallId, string ResultJson) : StreamEvent;

/// <summary>Tool завершился ошибкой или был отклонён пользователем.</summary>
public sealed record ToolCallFailedDto(string CallId, string ErrorMessage) : StreamEvent;

/// <summary>
/// Последний элемент потока — все сообщения сохранены в БД.
/// LastNodeId — ID последнего сохранённого узла (финальный ответ ассистента).
/// Consumer использует это для обновления ID последней UI-модели.
/// </summary>
public sealed record AssistantResponseSavedDto(Guid LastNodeId) : StreamEvent;
