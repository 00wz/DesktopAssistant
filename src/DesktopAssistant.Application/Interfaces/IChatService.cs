using DesktopAssistant.Application.Dtos;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Сервис для взаимодействия с LLM через чат
/// </summary>
public interface IChatService
{
    /// <summary>Создаёт новый диалог.</summary>
    Task<ConversationDto> CreateConversationAsync(string title, string? systemPrompt = null, CancellationToken cancellationToken = default);

    /// <summary>Получает все активные диалоги.</summary>
    Task<IEnumerable<ConversationDto>> GetConversationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Получает диалог по ID. Возвращает null если не найден.</summary>
    Task<ConversationDto?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получает историю сообщений текущей активной ветки диалога в виде DTO.
    /// Включает информацию о siblings для каждого узла.
    /// Системные сообщения не включаются.
    /// </summary>
    Task<IEnumerable<MessageDto>> GetConversationHistoryAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Переключается на альтернативную ветку (sibling) сообщения.</summary>
    Task SwitchToSiblingAsync(Guid conversationId, Guid parentNodeId, Guid newChildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет пользовательское сообщение как дочерний узел указанного родителя (для создания sibling).
    /// Возвращает DTO с информацией о siblings.
    /// </summary>
    Task<UserMessageDto> AddUserMessageAsync(Guid conversationId, Guid parentNodeId, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Формирует контекст начиная с указанного сообщения и выполняет запрос ассистенту.
    /// Возвращает IAsyncEnumerable поток событий:
    /// AssistantTurnDto → (ChunkReceived events) → ToolCallRequestedDto →
    /// AssistantResponseSavedDto → ...
    /// </summary>
    IAsyncEnumerable<StreamEvent> GetAssistantResponseAsync(Guid conversationId, Guid lastMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Выполняет ожидающий tool-вызов (pendingNodeId — ID узла с ToolNodeMetadata.ResultJson == null).
    /// Stateless: все данные восстанавливаются из БД по pendingNodeId.
    /// </summary>
    Task<ToolCallResult> ApproveToolCallAsync(Guid pendingNodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Отклоняет ожидающий tool-вызов.
    /// Обновляет узел статусом "Denied by user" и возвращает ToolCallResult.
    /// </summary>
    Task<ToolCallResult> DenyToolCallAsync(Guid pendingNodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Определяет текущее состояние диалога, начиная обход с lastNodeId.
    /// Возвращает <see cref="ConversationState"/>, описывающий доступные действия пользователя.
    /// </summary>
    Task<ConversationState> GetConversationStateAsync(Guid lastNodeId, CancellationToken cancellationToken = default);
}
