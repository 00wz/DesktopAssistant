using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Domain.Entities;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Сервис для взаимодействия с LLM через чат
/// </summary>
public interface IChatService
{
    /// <summary>Создаёт новый диалог.</summary>
    Task<Conversation> CreateConversationAsync(string title, string? systemPrompt = null, CancellationToken cancellationToken = default);

    /// <summary>Получает все активные диалоги.</summary>
    Task<IEnumerable<Conversation>> GetConversationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Получает историю сообщений текущей активной ветки диалога в виде DTO.
    /// Включает информацию о siblings для каждого узла.
    /// Системные сообщения не включаются.
    /// </summary>
    Task<IEnumerable<MessageDto>> GetConversationHistoryAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Переключается на альтернативную ветку (sibling) сообщения.</summary>
    Task SwitchToSiblingAsync(Guid conversationId, Guid parentNodeId, Guid newChildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет пользовательское сообщение в активную ветку диалога.
    /// Возвращает DTO с информацией о siblings.
    /// </summary>
    Task<UserMessageDto> AddUserMessageAsync(Guid conversationId, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет пользовательское сообщение как дочерний узел указанного родителя (для создания sibling).
    /// Возвращает DTO с информацией о siblings.
    /// </summary>
    Task<UserMessageDto> AddUserMessageAsync(Guid conversationId, Guid parentNodeId, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Формирует контекст активной ветки и выполняет запрос ассистенту.
    /// Возвращает IAsyncEnumerable поток событий:
    /// AssistantTurnDto → (ChunkReceived events) → ToolCallRequestedDto → ToolCallExecutingDto →
    /// ToolCallCompletedDto/ToolCallFailedDto → AssistantTurnDto → ...
    /// </summary>
    IAsyncEnumerable<StreamEvent> GetAssistantResponseAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Формирует контекст начиная с указанного сообщения и выполняет запрос ассистенту.
    /// Используется при создании sibling ветки (после редактирования сообщения).
    /// </summary>
    IAsyncEnumerable<StreamEvent> GetAssistantResponseAsync(Guid conversationId, Guid lastMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Выполняет ожидающий tool-вызов (pendingNodeId — ID узла с Content == "__PENDING_TOOL__").
    /// Stateless: все данные восстанавливаются из БД по pendingNodeId.
    /// Обновляет узел результатом и возвращает ToolCallResult с флагом AllToolsForTurnCompleted.
    /// </summary>
    Task<ToolCallResult> ApproveToolCallAsync(Guid pendingNodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Отклоняет ожидающий tool-вызов.
    /// Обновляет узел статусом "Denied by user" и возвращает ToolCallResult.
    /// </summary>
    Task<ToolCallResult> DenyToolCallAsync(Guid pendingNodeId, CancellationToken cancellationToken = default);
}
