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
        Guid profileId,
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
                var title = name
                    ?? (firstMessage.Length > 60 ? firstMessage[..60] + "…" : firstMessage);

                conversation = await chatService.CreateSubagentConversationAsync(
                    parentConversationId, toolNodeId, title, profileId,
                    systemPrompt ?? string.Empty, canSpawnSubagents, ct);

                isNew = true;
            }
        }

        var result = await ResumeOrStartAsync(conversation.Id, isNew, firstMessage, ct);
        return $"Sub-agent ID: {conversation.Id}\n\n{result}";
    }

    /// <inheritdoc />
    public async Task<string> SendMessageToSubagentAsync(
        Guid conversationId,
        Guid toolNodeId,
        string message,
        CancellationToken ct = default)
    {
        ConversationDto conversation;
        using (var scope = _scopeFactory.CreateScope())
        {
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            conversation = await chatService.GetConversationAsync(conversationId, ct)
                ?? throw new InvalidOperationException($"Sub-agent conversation {conversationId} not found.");
        }

        // Retry/resume path: same tool call that was interrupted
        if (toolNodeId == conversation.SpawnedByToolNodeId)
        {
            _logger.LogInformation(
                "send_message_to_subagent: detected retry for toolNode={ToolNodeId}, resuming sub-agent {ConversationId}",
                toolNodeId, conversationId);
            return await ResumeOrStartAsync(conversationId, isNew: false, firstMessage: message, ct);
        }

        // New send operation: validate state strictly, then hand over ownership to this tool call
        var session = await _sessionService.GetOrCreate(conversationId);

        if (session.IsRunning)
            throw new InvalidOperationException(
                "Cannot send message: the sub-agent is currently processing.");

        if (session.State is not (ConversationState.LastMessageIsAssistant or ConversationState.AgentTaskCompleted))
            throw new InvalidOperationException(
                $"Cannot send message: sub-agent is in state {session.State}. " +
                "Expected LastMessageIsAssistant or AgentTaskCompleted.");

        // Transfer ownership to this tool node so retries are detected as resumes
        using (var scope = _scopeFactory.CreateScope())
        {
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            await chatService.UpdateSpawnedByToolNodeAsync(conversationId, toolNodeId, ct);
        }

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
    public async Task<IReadOnlyList<AssistantProfileDto>> GetAvailableProfilesAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var profileService = scope.ServiceProvider.GetRequiredService<IAssistantProfileService>();
        return (await profileService.GetAssistantProfilesAsync(ct)).ToList();
    }

    /// <inheritdoc />
    public void CompleteSubagent(Guid conversationId, string result)
    {
        if (_pending.TryRemove(conversationId, out var tcs))
        {
            _logger.LogInformation("Sub-agent {ConversationId} completed", conversationId);
            tcs.SetResult(result);
        }
    }

    // ── Shared resume/start logic ─────────────────────────────────────────────

    /// <summary>
    /// Core state-machine used by both RunSubagentAsync and the retry path of SendMessageToSubagentAsync.
    /// Handles all session states and awaits the completion TCS.
    /// <paramref name="firstMessage"/> is only used when <paramref name="isNew"/> is true.
    /// </summary>
    private async Task<string> ResumeOrStartAsync(
        Guid conversationId,
        bool isNew,
        string firstMessage,
        CancellationToken ct)
    {
        var session = await _sessionService.GetOrCreate(conversationId);

        if (session.State == ConversationState.AgentTaskCompleted)
        {
            _logger.LogInformation(
                "Sub-agent {ConversationId} already completed — returning cached result", conversationId);
            var cachedResult = await session.GetLastCompleteTaskResultAsync(ct);
            return cachedResult ?? "Task completed.";
        }

        if (session.IsRunning)
        {
            _logger.LogInformation(
                "Sub-agent {ConversationId} is already running — waiting for completion", conversationId);
            var runningTcs = _pending.GetOrAdd(
                conversationId,
                _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));
            try { return await runningTcs.Task.WaitAsync(ct); }
            catch (OperationCanceledException) { _pending.TryRemove(conversationId, out _); throw; }
        }

        var tcs = _pending.GetOrAdd(
            conversationId,
            _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));

        if (isNew)
        {
            _logger.LogInformation("Starting new sub-agent {ConversationId}", conversationId);
            _ = session.SendMessageAsync(firstMessage, ct: CancellationToken.None);
        }
        else
        {
            switch (session.State)
            {
                case ConversationState.LastMessageIsUser:
                case ConversationState.AllToolCallsCompleted:
                    _logger.LogInformation(
                        "Resuming sub-agent {ConversationId} (state={State})", conversationId, session.State);
                    _ = session.ResumeAsync(CancellationToken.None);
                    break;

                case ConversationState.HasPendingToolCalls:
                    _logger.LogInformation(
                        "Sub-agent {ConversationId} has pending tool calls — waiting", conversationId);
                    break;

                default:
                    _pending.TryRemove(conversationId, out _);
                    throw new InvalidOperationException(
                        $"Sub-agent {conversationId} is in unexpected state {session.State} and cannot be resumed.");
            }
        }

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
}
