namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Singleton-пул сессий диалогов.
/// Гарантирует, что для каждого диалога существует ровно одна активная сессия.
/// </summary>
public interface IConversationSessionService : IDisposable
{
    /// <summary>
    /// Снимок идентификаторов всех активных сессий на момент вызова.
    /// </summary>
    IReadOnlyCollection<Guid> ActiveSessionIds { get; }

    /// <summary>
    /// Вызывается после добавления новой сессии в пул.
    /// Аргумент — идентификатор диалога.
    /// </summary>
    event EventHandler<Guid> SessionCreated;

    /// <summary>
    /// Вызывается после удаления сессии из пула.
    /// Аргумент — идентификатор диалога.
    /// </summary>
    event EventHandler<Guid> SessionReleased;

    /// <summary>
    /// Возвращает существующую сессию или создаёт и инициализирует новую.
    /// </summary>
    Task<IConversationSession> GetOrCreate(Guid conversationId);

    /// <summary>
    /// Освобождает сессию диалога и удаляет её из пула.
    /// Вызывается при удалении диалога.
    /// </summary>
    void Release(Guid conversationId);
}
