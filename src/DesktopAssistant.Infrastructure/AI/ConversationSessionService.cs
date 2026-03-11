using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace DesktopAssistant.Infrastructure.AI;

internal class ConversationSessionService : IConversationSessionService
{
    private readonly ConcurrentDictionary<Guid, ConversationSession> _sessions = new();
    private IToolApprovalService _toolApprovalService;
    private ILoggerFactory _loggerFactory;
    private IChatService _chatService;

    public ConversationSessionService(
        ILoggerFactory loggerFactory,
        IToolApprovalService toolApprovalService,
        IChatService chatService)
    {
        _toolApprovalService = toolApprovalService;
        _loggerFactory = loggerFactory;
        _chatService = chatService;
    }

    public async Task<IConversationSession> GetOrCreate(Guid conversationId)
    {   
        if(_sessions.TryGetValue(conversationId, out var conversationSession)) 
        { 
            return conversationSession; 
        }

        var newSession = new ConversationSession(
            conversationId,
            _chatService,
            _loggerFactory.CreateLogger<ConversationSession>(),
            _toolApprovalService);

        await newSession.InitializeAsync();

        _sessions[conversationId] = newSession;

        return newSession;
    }

    public void Release(Guid conversationId)
    {
        if (_sessions.TryRemove(conversationId, out var session))
            session.Dispose();
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
        _sessions.Clear();
    }
}
