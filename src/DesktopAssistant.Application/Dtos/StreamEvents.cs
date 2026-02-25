namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Базовый тип для элементов IAsyncEnumerable-потока от LLM.
/// Каждый элемент потока GetAssistantResponseAsync — один из подтипов.
/// </summary>
public abstract record StreamEvent;

/// <summary>
/// Начало нового тёрна ассистента. Один объект на весь тёрн — не на чанк.
/// </summary>
public sealed record AssistantTurnDto : StreamEvent
{
    public Guid TempId { get; } = Guid.NewGuid();
    public DateTime StartedAt { get; } = DateTime.UtcNow;
}

/// <summary>
/// Текстовый чанк текущего тёрна ассистента.
/// Yielded по одному на каждый непустой чанк из LLM.
/// </summary>
public sealed record AssistantChunkDto(string Text) : StreamEvent;

/// <summary>
/// Tool-вызов требует подтверждения пользователя.
/// PendingNodeId — ID узла в БД с Content == "__PENDING_TOOL__".
/// Consumer создаёт карточку и вызывает ApproveToolCallAsync / DenyToolCallAsync по действию пользователя.
/// </summary>
public sealed record ToolCallRequestedDto(
    string CallId,
    string PluginName,
    string FunctionName,
    string ArgumentsJson,
    Guid PendingNodeId) : StreamEvent;

/// <summary>
/// Последний элемент тёрна — сообщение ассистента сохранено в БД.
/// LastNodeId — ID сохранённого узла ассистента.
/// Если в ответе есть tool-вызовы, этот ивент приходит ДО ToolCallRequestedDto.
/// </summary>
public sealed record AssistantResponseSavedDto(Guid LastNodeId) : StreamEvent;
