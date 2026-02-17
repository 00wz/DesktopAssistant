using DesktopAssistant.Domain.Entities;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Сервис для взаимодействия с LLM через чат
/// </summary>
public interface IChatService
{
  
    /// <summary>
    /// Создаёт новый диалог
    /// </summary>
    /// <param name="title">Название диалога</param>
    /// <param name="systemPrompt">Системный промпт (опционально)</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Созданный диалог</returns>
    Task<Conversation> CreateConversationAsync(string title, string? systemPrompt = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Получает все диалоги
    /// </summary>
    Task<IEnumerable<Conversation>> GetConversationsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Получает историю сообщений для текущей ветки диалога
    /// </summary>
    /// <param name="conversationId">ID диалога</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Список сообщений от корня до текущей головы</returns>
    Task<IEnumerable<MessageNode>> GetConversationHistoryAsync(Guid conversationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Переключается на альтернативную ветку (sibling) сообщения
    /// </summary>
    /// <param name="conversationId">ID диалога</param>
    /// <param name="parentNodeId">ID родительского узла</param>
    /// <param name="newChildId">ID нового активного дочернего узла</param>
    /// <param name="cancellationToken">Токен отмены</param>
    Task SwitchToSiblingAsync(Guid conversationId, Guid parentNodeId, Guid newChildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет пользовательское сообщение в диалог и обновляет активную ветку
    /// </summary>
    /// <param name="conversationId">ID диалога</param>
    /// <param name="parentNodeId">ID родительского сообщения</param>
    /// <param name="content">Содержимое сообщения</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Созданное сообщение</returns>
    Task<MessageNode> AddUserMessageAsync(
        Guid conversationId,
        Guid parentNodeId,
        string content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет пользовательское сообщение в активную ветку диалога
    /// </summary>
    /// <param name="conversationId">ID диалога</param>
    /// <param name="content">Содержимое сообщения</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Созданное сообщение</returns>
    Task<MessageNode> AddUserMessageAsync(
        Guid conversationId,
        string content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Формирует контекст истории диалога по <paramref name="lastMessageId"/> и выполняет запрос ассистенту
    /// </summary>
    /// <param name="conversationId">ID диалога</param>
    /// <param name="lastMessageId">ID последнего сообщения для построения контекста</param>
    /// <param name="onChunkReceived">Callback для обработки частей ответа (опционально, для streaming)</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Ответ ассистента</returns>
    Task<MessageNode> GetAssistantResponseAsync(
        Guid conversationId,
        Guid lastMessageId,
        Action<string>? onChunkReceived = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Формирует контекст текущей активной ветки диалога и выполняет запрос ассистенту
    /// </summary>
    /// <param name="conversationId">ID диалога</param>
    /// <param name="onChunkReceived">Callback для обработки частей ответа (опционально, для streaming)</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Ответ ассистента</returns>
    Task<MessageNode> GetAssistantResponseAsync(
        Guid conversationId,
        Action<string>? onChunkReceived = null,
        CancellationToken cancellationToken = default);
}
