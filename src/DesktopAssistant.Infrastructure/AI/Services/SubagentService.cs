using System.Collections.Concurrent;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.Infrastructure.AI.Services;

/// <summary>
/// Manages creation, resumption, and completion tracking for sub-agent conversations.
/// Singleton — holds in-memory TaskCompletionSource registry keyed by conversation ID.
/// </summary>
public class SubagentService : ISubagentService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConversationSessionService _sessionService;
    private readonly ILogger<SubagentService> _logger;

    /// <summary>Pending awaiters: conversationId → TCS that resolves when complete_task is called.</summary>
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<string>> _pending = new();

    public SubagentService(
        IServiceScopeFactory scopeFactory,
        IConversationSessionService sessionService,
        ILogger<SubagentService> logger)
    {
        _scopeFactory = scopeFactory;
        _sessionService = sessionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> RunSubagentAsync(
        Guid parentConversationId,
        Guid toolNodeId,
        string firstMessage,
        string? systemPrompt,
        bool canSpawnSubagents,
        string? name,
        CancellationToken ct = default)
    {
        ConversationDto conversation;
        bool isNew;

        using (var scope = _scopeFactory.CreateScope())
        {
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

            var existing = await chatService.GetConversationByToolNodeIdAsync(toolNodeId, ct);
            if (existing != null)
            {
                conversation = existing;
                isNew = false;
            }
            else
            {
                var parent = await chatService.GetConversationAsync(parentConversationId, ct)
                    ?? throw new InvalidOperationException(
                        $"Parent conversation {parentConversationId} not found.");

                var profileId = parent.AssistantProfileId
                    ?? throw new InvalidOperationException(
                        $"Parent conversation {parentConversationId} has no assistant profile.");

                var title = name
                    ?? (firstMessage.Length > 60 ? firstMessage[..60] + "…" : firstMessage);

                conversation = await chatService.CreateSubagentConversationAsync(
                    parentConversationId, toolNodeId, title, profileId,
                    systemPrompt ?? string.Empty, canSpawnSubagents, ct);

                isNew = true;
            }
        }

        var session = await _sessionService.GetOrCreate(conversation.Id);

        // If already completed, return the cached result without registering a TCS
        if (session.State == ConversationState.AgentTaskCompleted)
        {
            _logger.LogInformation(
                "Sub-agent {ConversationId} already completed — returning cached result", conversation.Id);

            var cachedResult = await session.GetLastCompleteTaskResultAsync(ct);
            return cachedResult ?? "Task completed.";
        }

        if (session.IsRunning)
        {
            _logger.LogInformation(
                "Sub-agent {ConversationId} is already running — waiting for completion", conversation.Id);
            // Already progressing; just register a TCS and wait for complete_task
            var runningTcs = _pending.GetOrAdd(
                conversation.Id,
                _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));
            try { return await runningTcs.Task.WaitAsync(ct); }
            catch (OperationCanceledException) { _pending.TryRemove(conversation.Id, out _); throw; }
        }

        var tcs = _pending.GetOrAdd(
            conversation.Id,
            _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));

        if (isNew)
        {
            _logger.LogInformation(
                "Starting new sub-agent {ConversationId} (toolNode={ToolNodeId})", conversation.Id, toolNodeId);
            _ = session.SendMessageAsync(firstMessage, ct: CancellationToken.None);
        }
        else
        {
            switch (session.State)
            {
                case ConversationState.LastMessageIsUser:
                case ConversationState.AllToolCallsCompleted:
                    _logger.LogInformation(
                        "Resuming sub-agent {ConversationId} (state={State})", conversation.Id, session.State);
                    _ = session.ResumeAsync(CancellationToken.None);
                    break;

                case ConversationState.HasPendingToolCalls:
                    // Tools are executing concurrently; complete_task will fire once they finish.
                    _logger.LogInformation(
                        "Sub-agent {ConversationId} has pending tool calls — waiting", conversation.Id);
                    break;

                default:
                    _pending.TryRemove(conversation.Id, out _);
                    throw new InvalidOperationException(
                        $"Sub-agent {conversation.Id} is in unexpected state {session.State} and cannot be resumed.");
            }
        }

        try
        {
            return await tcs.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(conversation.Id, out _);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> SendMessageToSubagentAsync(
        Guid conversationId,
        string message,
        CancellationToken ct = default)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            _ = await chatService.GetConversationAsync(conversationId, ct)
                ?? throw new InvalidOperationException($"Sub-agent conversation {conversationId} not found.");
        }

        var session = await _sessionService.GetOrCreate(conversationId);

        if (session.IsRunning)
            throw new InvalidOperationException(
                "Cannot send message: the sub-agent is currently processing.");

        if (session.State is not (ConversationState.LastMessageIsAssistant or ConversationState.AgentTaskCompleted))
            throw new InvalidOperationException(
                $"Cannot send message: sub-agent is in state {session.State}. " +
                "Expected LastMessageIsAssistant or AgentTaskCompleted.");

        var tcs = _pending.GetOrAdd(
            conversationId,
            _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));

        _ = session.SendMessageAsync(message, ct: CancellationToken.None);

        try
        {
            return await tcs.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(conversationId, out _);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubagentInfoDto>> GetSubagentsAsync(
        Guid parentConversationId,
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
        return await chatService.GetSubagentConversationsAsync(parentConversationId, ct);
    }

    /// <inheritdoc />
    public void CompleteSubagent(Guid conversationId, string result)
    {
        if (_pending.TryRemove(conversationId, out var tcs))
        {
            _logger.LogInformation("Sub-agent {ConversationId} completed with result: {Result}",
                conversationId, result.Length > 100 ? result[..100] + "…" : result);
            tcs.SetResult(result);
        }
    }
}
