using System.Collections.Concurrent;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.Infrastructure.AI.Session;

internal class ConversationSession : IConversationSession
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConversationSession> _logger;
    private readonly IToolApprovalService _toolApprovalService;

    /// <summary>Prevents multiple LLM turns from running concurrently.</summary>
    private readonly SemaphoreSlim _runLock = new(1, 1);

    /// <summary>
    /// Counter of tool calls currently in progress (approve/deny started, result not yet processed).
    /// Access only via <see cref="Interlocked"/> — tool calls execute concurrently.
    /// </summary>
    private int _activeToolCount;

    /// <summary>
    /// IDs of assistant nodes for which the next turn has already been started.
    /// Access only under <see cref="_runLock"/>.
    /// </summary>
    private readonly HashSet<Guid> _startedTurns = [];

    /// <summary>
    /// Cancels all active Approve/Deny operations on session re-initialization
    /// (e.g. when switching to another conversation branch).
    /// </summary>
    private CancellationTokenSource _toolsCancellationTokenSource = new();

    public Guid ConversationId { get; }
    public Guid CurrentLeafNodeId { get; private set; }
    public ConversationState State { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsExecutingTools => _activeToolCount > 0;

    public event EventHandler<SessionEvent>? EventOccurred;

    public ConversationSession(
        Guid conversationId,
        IServiceScopeFactory scopeFactory,
        ILogger<ConversationSession> logger,
        IToolApprovalService toolApprovalService)
    {
        ConversationId = conversationId;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _toolApprovalService = toolApprovalService;
    }

    // ── Initialization ───────────────────────────────────────────────────────

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (!await _runLock.WaitAsync(0, ct))
            throw new InvalidOperationException("Cannot initialize: another LLM turn is already in progress.");
        try
        {
            await InitializeInternalAsync(ct);
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task InitializeInternalAsync(CancellationToken ct = default)
    {
        // Cancel all incomplete Approve/Deny operations from the previous state
        _toolsCancellationTokenSource.Cancel();
        _toolsCancellationTokenSource.Dispose();
        _toolsCancellationTokenSource = new CancellationTokenSource();
        _startedTurns.Clear();

        // Reset the active tool call counter.
        // Cancelled tasks may still reach their finally blocks and call DecrementActiveTools —
        // underflow protection is handled there.
        if (Interlocked.Exchange(ref _activeToolCount, 0) > 0)
            Emit(new ToolExecutionStateChangedSessionEvent(false));

        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

        var conversation = await chatService.GetConversationAsync(ConversationId, ct)
            ?? throw new InvalidOperationException($"Conversation {ConversationId} not found.");

        CurrentLeafNodeId = conversation.ActiveLeafNodeId
            ?? throw new InvalidOperationException($"Conversation {ConversationId} has no active leaf node.");

        var state = await chatService.GetConversationStateAsync(CurrentLeafNodeId, ct);
        UpdateState(state);

        Emit(new InitializeSessionEvent());
    }

    // ── LLM operations ───────────────────────────────────────────────────────

    public async Task SendMessageAsync(string message, Guid? parentNodeId = null, CancellationToken ct = default)
    {
        if (!await _runLock.WaitAsync(0, ct))
            throw new InvalidOperationException("Cannot send message: another LLM turn is already in progress.");
        try
        {
            SetRunning(true);

            // targetParentId is computed under the lock so CurrentLeafNodeId cannot change before use
            var targetParentId = parentNodeId ?? CurrentLeafNodeId;

            // If parentNodeId differs from the current leaf — check the state of that branch separately
            ConversationState parentState;
            if (targetParentId == CurrentLeafNodeId)
            {
                parentState = State;
            }
            else
            {
                using var stateScope = _scopeFactory.CreateScope();
                var stateChatService = stateScope.ServiceProvider.GetRequiredService<IChatService>();
                parentState = await stateChatService.GetConversationStateAsync(targetParentId, ct);
            }

            if (parentState != ConversationState.LastMessageIsAssistant)
                throw new InvalidOperationException(
                    $"Cannot send message: the target node must be in state {ConversationState.LastMessageIsAssistant}, " +
                    $"but got {parentState}.");

            using var scope = _scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

            var userDto = await chatService.AddUserMessageAsync(
                ConversationId,
                targetParentId,
                message,
                cancellationToken: ct);

            if (targetParentId != CurrentLeafNodeId)
            {
                // Adding to a non-current branch (message editing) → re-initialize the session
                await InitializeInternalAsync(ct);
            }
            else
            {
                CurrentLeafNodeId = userDto.Id;
                Emit(new UserMessageAddedSessionEvent(userDto));

                var newState = await chatService.GetConversationStateAsync(CurrentLeafNodeId, ct);
                UpdateState(newState);
            }

            await RunLlmTurnAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message in conversation {ConversationId}", ConversationId);
            Emit(new SessionErrorEvent(ex.Message, ex));
        }
        finally
        {
            SetRunning(false);
            _runLock.Release();
        }
    }

    public async Task ResumeAsync(CancellationToken ct = default)
    {
        if (!await _runLock.WaitAsync(0, ct))
            throw new InvalidOperationException("Cannot resume: another LLM turn is already in progress.");
        try
        {
            SetRunning(true);

            if (State != ConversationState.LastMessageIsUser && State != ConversationState.AllToolCallsCompleted)
                throw new InvalidOperationException(
                    $"Cannot resume: expected state {ConversationState.LastMessageIsUser} or " +
                    $"{ConversationState.AllToolCallsCompleted}, but got {State}.");

            await RunLlmTurnAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resuming conversation {ConversationId}", ConversationId);
            Emit(new SessionErrorEvent(ex.Message, ex));
        }
        finally
        {
            SetRunning(false);
            _runLock.Release();
        }
    }

    private async Task RunLlmTurnAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

        // Start handlers only after streaming completes — all tool nodes are already in the DB.
        var pendingToolRequests = new List<ToolCallRequestedDto>();

        await foreach (var evt in chatService.GetAssistantResponseAsync(ConversationId, CurrentLeafNodeId, ct))
        {
            switch (evt)
            {
                case AssistantTurnDto turn:
                    Emit(new AssistantTurnStartedSessionEvent(turn.TempId, turn.StartedAt));
                    break;
                case AssistantChunkDto chunk:
                    Emit(new AssistantChunkSessionEvent(chunk.Text));
                    break;
                case AssistantResponseSavedDto saved:
                    CurrentLeafNodeId = saved.LastNodeId;
                    Emit(new AssistantResponseSavedSessionEvent(saved.LastNodeId, saved.TotalTokenCount));
                    break;
                case ToolCallRequestedDto toolReq:
                    CurrentLeafNodeId = toolReq.PendingNodeId;
                    pendingToolRequests.Add(toolReq);
                    break;
            }
        }

        foreach (var toolReq in pendingToolRequests)
            _ = HandleToolCallRequestedAsync(toolReq, ct);

        using var stateScope = _scopeFactory.CreateScope();
        var stateChatService = stateScope.ServiceProvider.GetRequiredService<IChatService>();
        var state = await stateChatService.GetConversationStateAsync(CurrentLeafNodeId, ct);
        UpdateState(state);
    }

    /// <summary>
    /// Handles an incoming tool call: determines whether auto-approval is needed,
    /// publishes <see cref="ToolRequestedSessionEvent"/>, and auto-approves if required.
    /// Invoked fire-and-forget — all exceptions are handled internally.
    /// </summary>
    private async Task HandleToolCallRequestedAsync(ToolCallRequestedDto toolReq, CancellationToken ct)
    {
        try
        {
            var isAutoApproved = await _toolApprovalService.IsAutoApprovedAsync(
                toolReq.PluginName, toolReq.FunctionName);

            Emit(new ToolRequestedSessionEvent(
                PendingNodeId: toolReq.PendingNodeId,
                CallId: toolReq.CallId,
                PluginName: toolReq.PluginName,
                FunctionName: toolReq.FunctionName,
                ArgumentsJson: toolReq.ArgumentsJson,
                IsAutoApproved: isAutoApproved));

            if (isAutoApproved)
                await ApproveToolAsync(toolReq.PendingNodeId, ct);
        }
        catch (Exception ex)
        {
            // Catch all exceptions (including OperationCanceledException),
            // because the method is called fire-and-forget — an unhandled exception
            // would result in an UnobservedTaskException.
            _logger.LogError(ex,
                "Error handling tool call {PluginName}.{FunctionName} in conversation {ConversationId}",
                toolReq.PluginName, toolReq.FunctionName, ConversationId);
            Emit(new SessionErrorEvent(ex.Message, ex));
        }
    }

    public async Task ApproveToolAsync(Guid pendingNodeId, CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct, _toolsCancellationTokenSource.Token);

        ToolCallResult result;
        IncrementActiveTools();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            result = await chatService.ApproveToolCallAsync(pendingNodeId, linkedCts.Token);
        }
        finally
        {
            DecrementActiveTools();
        }

        await HandleToolCallResultAsync(pendingNodeId, result, linkedCts.Token);
    }

    public async Task DenyToolAsync(Guid pendingNodeId, CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct, _toolsCancellationTokenSource.Token);

        ToolCallResult result;
        IncrementActiveTools();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            result = await chatService.DenyToolCallAsync(pendingNodeId, linkedCts.Token);
        }
        finally
        {
            DecrementActiveTools();
        }

        await HandleToolCallResultAsync(pendingNodeId, result, linkedCts.Token);
    }

    private async Task HandleToolCallResultAsync(
        Guid pendingNodeId,
        ToolCallResult toolCallResult,
        CancellationToken ct)
    {
        try
        {
            Emit(new ToolResultSessionEvent(pendingNodeId, toolCallResult.ResultJson, toolCallResult.Status));

            using var scope = _scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            var state = await chatService.GetConversationStateAsync(CurrentLeafNodeId, ct);
            UpdateState(state);

            if (state != ConversationState.AllToolCallsCompleted) return;

            await _runLock.WaitAsync(ct);
            try
            {
                // Deduplication: one turn per LLM response. AssistantNodeId == default
                // means a legacy node without the field — require manual Resume.
                if (toolCallResult.AssistantNodeId == default
                    || !_startedTurns.Add(toolCallResult.AssistantNodeId))
                    return;

                SetRunning(true);
                await RunLlmTurnAsync(ct);
            }
            finally
            {
                SetRunning(false);
                _runLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling tool result {PendingNodeId} in conversation {ConversationId}",
                pendingNodeId, ConversationId);
            Emit(new SessionErrorEvent(ex.Message, ex));
        }
    }

    // ── Data facade ──────────────────────────────────────────────────────────

    public async Task<IEnumerable<MessageDto>> LoadHistoryAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
        return await chatService.GetConversationHistoryAsync(ConversationId, CurrentLeafNodeId, ct);
    }

    public async Task SummarizeAsync(Guid selectedNodeId, CancellationToken ct = default)
    {
        if (!await _runLock.WaitAsync(0, ct))
            throw new InvalidOperationException("Cannot summarize: another LLM turn is already in progress.");
        try
        {
            SetRunning(true);

            using var scope = _scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            var state = selectedNodeId == CurrentLeafNodeId ? State : await chatService.GetConversationStateAsync(selectedNodeId, ct);

            if (state is ConversationState.HasPendingToolCalls)
                throw new InvalidOperationException(
                    "Cannot summarize: one or more tool calls are still pending approval. " +
                    "Approve or deny them first, then try again.");
            if (state is ConversationState.ToolCallIdMismatch)
                throw new InvalidOperationException(
                    "Cannot summarize at this point: the selected message is in the middle of a tool call sequence. " +
                    "Select a message before or after the entire tool call group.");

            await foreach (var evt in chatService.SummarizeAsync(ConversationId, selectedNodeId, ct))
            {
                SessionEvent sessionEvent = evt switch
                {
                    SummarizationStartedDto started => new SummarizationStartedSessionEvent(started.ParentNodeId),
                    SummarizationCompletedDto completed => new SummarizationCompletedSessionEvent(
                        completed.ParentNodeId, completed.SummaryNodeId, completed.SummaryContent),
                    _ => new SessionErrorEvent($"Unknown summarization event: {evt.GetType().Name}")
                };
                Emit(sessionEvent);
            }

            await InitializeInternalAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error summarizing node {NodeId} in conversation {ConversationId}",
                selectedNodeId, ConversationId);
            Emit(new SessionErrorEvent(ex.Message, ex));
        }
        finally
        {
            SetRunning(false);
            _runLock.Release();
        }
    }

    public async Task SwitchToSiblingAsync(Guid parentNodeId, Guid newChildId, CancellationToken ct = default)
    {
        if (!await _runLock.WaitAsync(0, ct))
            throw new InvalidOperationException("Cannot switch sibling: another LLM turn is already in progress.");
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            await chatService.SwitchToSiblingAsync(ConversationId, parentNodeId, newChildId, cancellationToken: ct);
            await InitializeInternalAsync(ct);
        }
        finally
        {
            _runLock.Release();
        }
    }

    public async Task UpdateSystemPromptAsync(string systemPrompt, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
        await chatService.UpdateConversationSystemPromptAsync(ConversationId, systemPrompt, ct);
    }

    public async Task<ConversationSettingsDto?> GetSettingsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
        return await chatService.GetConversationSettingsAsync(ConversationId, ct);
    }

    public async Task ChangeProfileAsync(Guid profileId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
        await chatService.ChangeConversationProfileAsync(ConversationId, profileId, ct);
    }

    public async Task ChangeModeAsync(ConversationMode mode, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
        await chatService.ChangeConversationModeAsync(ConversationId, mode, ct);
    }

    // ── Helper methods ───────────────────────────────────────────────────────

    private void UpdateState(ConversationState state)
    {
        State = state;
        Emit(new ConversationStateChangedSessionEvent(state));
    }

    private void SetRunning(bool value)
    {
        IsRunning = value;
        Emit(new RunningStateChangedSessionEvent(value));
    }

    private void IncrementActiveTools()
    {
        if (Interlocked.Increment(ref _activeToolCount) == 1)
            Emit(new ToolExecutionStateChangedSessionEvent(true));
    }

    private void DecrementActiveTools()
    {
        var newCount = Interlocked.Decrement(ref _activeToolCount);
        if (newCount == 0)
            Emit(new ToolExecutionStateChangedSessionEvent(false));
        else if (newCount < 0)
            // Stale decrement after reset in InitializeInternalAsync — restore to 0, do not emit event.
            Interlocked.Exchange(ref _activeToolCount, 0);
    }

    private void Emit(SessionEvent evt) => EventOccurred?.Invoke(this, evt);

    public void Dispose()
    {
        _toolsCancellationTokenSource.Cancel();
        _toolsCancellationTokenSource.Dispose();
        _runLock.Dispose();
    }
}
