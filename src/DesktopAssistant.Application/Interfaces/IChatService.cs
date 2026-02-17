using DesktopAssistant.Domain.Entities;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Сервис для взаимодействия с LLM через чат
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Отправляет сообщение и получает ответ от LLM
    /// </summary>
    /// <param name="conversationId">ID диалога</param>
    /// <param name="userMessage">Сообщение пользователя</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Ответ от LLM</returns>
    Task<MessageNode> SendMessageAsync(Guid conversationId, string userMessage, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Отправляет сообщение и получает ответ от LLM с потоковой передачей
    /// </summary>
    /// <param name="conversationId">ID диалога</param>
    /// <param name="userMessage">Сообщение пользователя</param>
    /// <param name="onChunkReceived">Callback для обработки частей ответа</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Полный ответ от LLM</returns>
    Task<MessageNode> SendMessageStreamingAsync(
        Guid conversationId, 
        string userMessage, 
        Action<string> onChunkReceived,
        CancellationToken cancellationToken = default);
    
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
    /// Получает историю сообщений для диалога
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
}
