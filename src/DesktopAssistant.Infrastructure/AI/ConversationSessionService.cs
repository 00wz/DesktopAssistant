using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace DesktopAssistant.Infrastructure.AI;

internal class ConversationSessionService : IConversationSessionService
{
    private readonly ConcurrentDictionary<Guid, ConversationSession> _sessions = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IToolApprovalService _toolApprovalService;
    private readonly ILoggerFactory _loggerFactory;

    public IReadOnlyCollection<Guid> ActiveSessionIds => [.. _sessions.Keys];

    public event EventHandler<Guid>? SessionCreated;
    public event EventHandler<Guid>? SessionReleased;

    public ConversationSessionService(
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory,
        IToolApprovalService toolApprovalService)
    {
        _scopeFactory = scopeFactory;
        _loggerFactory = loggerFactory;
        _toolApprovalService = toolApprovalService;
    }

    public async Task<IConversationSession> GetOrCreate(Guid conversationId)
    {
        if (_sessions.TryGetValue(conversationId, out var existing))
            return existing;

        var newSession = new ConversationSession(
            conversationId,
            _scopeFactory,
            _loggerFactory.CreateLogger<ConversationSession>(),
            _toolApprovalService);

        await newSession.InitializeAsync();

        // Если параллельный вызов успел добавить сессию первым — используем её,
        // а только что созданную освобождаем
        var session = _sessions.GetOrAdd(conversationId, newSession);
        if (!ReferenceEquals(session, newSession))
        {
            newSession.Dispose();
            return session;
        }

        SessionCreated?.Invoke(this, conversationId);
        return session;
    }

    public void Release(Guid conversationId)
    {
        if (_sessions.TryRemove(conversationId, out var session))
        {
            session.Dispose();
            SessionReleased?.Invoke(this, conversationId);
        }
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
            session.Dispose();
        _sessions.Clear();
    }
}
