using DesktopAssistant.Domain.Entities;

namespace DesktopAssistant.Domain.Interfaces;

/// <summary>
/// Базовый интерфейс репозитория
/// </summary>
public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<T> AddAsync(T entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий для работы с диалогами
/// </summary>
public interface IConversationRepository : IRepository<Conversation>
{
    Task<IEnumerable<Conversation>> GetByAssistantIdAsync(Guid assistantId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Conversation>> GetActiveConversationsAsync(CancellationToken cancellationToken = default);
    Task<Conversation?> GetWithMessagesAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий для работы с узлами сообщений
/// </summary>
public interface IMessageNodeRepository : IRepository<MessageNode>
{
    Task<IEnumerable<MessageNode>> GetByConversationIdAsync(Guid conversationId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<MessageNode> TraverseToRootAsync(Guid nodeId, CancellationToken cancellationToken = default);
    Task<IEnumerable<MessageNode>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий для работы с профилями ассистентов
/// </summary>
public interface IAssistantProfileRepository : IRepository<AssistantProfile>
{
    Task<AssistantProfile?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий для работы с настройками приложения
/// </summary>
public interface IAppSettingsRepository
{
    Task<AppSettings?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<IEnumerable<AppSettings>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, string? description = null, CancellationToken cancellationToken = default);
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);
}
