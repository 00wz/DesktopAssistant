using DesktopAssistant.Domain.Entities;

namespace DesktopAssistant.Domain.Interfaces;

/// <summary>
/// Base repository interface.
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
/// Repository for working with conversations.
/// </summary>
public interface IConversationRepository : IRepository<Conversation>
{
    Task<IEnumerable<Conversation>> GetByAssistantIdAsync(Guid assistantId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Conversation>> GetActiveConversationsAsync(CancellationToken cancellationToken = default);
    Task<Conversation?> GetWithMessagesAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository for working with message nodes.
/// </summary>
public interface IMessageNodeRepository : IRepository<MessageNode>
{
    Task<IEnumerable<MessageNode>> GetByConversationIdAsync(Guid conversationId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<MessageNode> TraverseToRootAsync(Guid nodeId, CancellationToken cancellationToken = default);
    Task<IEnumerable<MessageNode>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository for working with assistant profiles.
/// </summary>
public interface IAssistantProfileRepository : IRepository<AssistantProfile>
{
}

/// <summary>
/// Repository for working with application settings.
/// </summary>
public interface IAppSettingsRepository
{
    Task<AppSettings?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<IEnumerable<AppSettings>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, string? description = null, CancellationToken cancellationToken = default);
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);
}
