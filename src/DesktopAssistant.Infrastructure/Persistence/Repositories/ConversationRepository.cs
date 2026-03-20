using System.Runtime.CompilerServices;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DesktopAssistant.Infrastructure.Persistence.Repositories;

public class ConversationRepository : BaseRepository<Conversation>, IConversationRepository
{
    public ConversationRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Conversation>> GetByAssistantIdAsync(
        Guid assistantId, 
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(c => c.AssistantProfileId == assistantId)
            .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Conversation>> GetActiveConversationsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(c => c.AssistantProfile)
            .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Conversation?> GetWithMessagesAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(c => c.Messages)
            .Include(c => c.AssistantProfile)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Conversation?> GetBySpawnedToolNodeAsync(
        Guid toolNodeId,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(c => c.SpawnedByToolNodeId == toolNodeId, cancellationToken);
    }

    public async Task<IEnumerable<Conversation>> GetSubagentsAsync(
        Guid parentConversationId,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(c => c.ParentConversationId == parentConversationId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}

public class MessageNodeRepository : BaseRepository<MessageNode>, IMessageNodeRepository
{
    public MessageNodeRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<MessageNode>> GetByConversationIdAsync(
        Guid conversationId, 
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async IAsyncEnumerable<MessageNode> TraverseToRootAsync(
        Guid nodeId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var currentNode = await _dbSet.FirstOrDefaultAsync(m => m.Id == nodeId, cancellationToken);

        while (currentNode != null)
        {
            yield return currentNode;
            currentNode = currentNode.ParentId.HasValue
                ? await _dbSet.FirstOrDefaultAsync(m => m.Id == currentNode.ParentId.Value, cancellationToken)
                : null;
        }
    }

    public async Task<IEnumerable<MessageNode>> GetChildrenAsync(
        Guid parentId, 
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(m => m.ParentId == parentId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}

public class AssistantProfileRepository : BaseRepository<AssistantProfile>, IAssistantProfileRepository
{
    public AssistantProfileRepository(AppDbContext context) : base(context)
    {
    }

}

public class AppSettingsRepository : IAppSettingsRepository
{
    private readonly AppDbContext _context;

    public AppSettingsRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<AppSettings?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return await _context.AppSettings
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
    }

    public async Task<IEnumerable<AppSettings>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.AppSettings.ToListAsync(cancellationToken);
    }

    public async Task SetAsync(
        string key, 
        string value, 
        string? description = null, 
        CancellationToken cancellationToken = default)
    {
        var existing = await GetByKeyAsync(key, cancellationToken);
        if (existing != null)
        {
            existing.UpdateValue(value);
        }
        else
        {
            var setting = new AppSettings(key, value, description);
            await _context.AppSettings.AddAsync(setting, cancellationToken);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var setting = await GetByKeyAsync(key, cancellationToken);
        return setting?.Value;
    }
}
