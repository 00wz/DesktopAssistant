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
            .Include(c => c.Branches)
            .Include(c => c.AssistantProfile)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
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

    public async Task<IEnumerable<MessageNode>> GetBranchPathAsync(
        Guid nodeId, 
        CancellationToken cancellationToken = default)
    {
        var path = new List<MessageNode>();
        var currentNode = await _dbSet.FirstOrDefaultAsync(m => m.Id == nodeId, cancellationToken);

        while (currentNode != null)
        {
            path.Insert(0, currentNode);
            if (currentNode.ParentId.HasValue)
            {
                currentNode = await _dbSet.FirstOrDefaultAsync(
                    m => m.Id == currentNode.ParentId.Value, 
                    cancellationToken);
            }
            else
            {
                currentNode = null;
            }
        }

        return path;
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

public class ConversationBranchRepository : BaseRepository<ConversationBranch>, IConversationBranchRepository
{
    public ConversationBranchRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<ConversationBranch>> GetByConversationIdAsync(
        Guid conversationId, 
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(b => b.ConversationId == conversationId)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ConversationBranch?> GetDefaultBranchAsync(
        Guid conversationId, 
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(b => b.ConversationId == conversationId && b.IsDefault, cancellationToken);
    }
}

public class AssistantProfileRepository : BaseRepository<AssistantProfile>, IAssistantProfileRepository
{
    public AssistantProfileRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<AssistantProfile?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(a => a.IsDefault, cancellationToken);
    }

    public async Task<AssistantProfile?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(a => a.Name == name, cancellationToken);
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
