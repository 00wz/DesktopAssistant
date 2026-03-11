using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.Infrastructure.AI;

internal class ConversationSession : IConversationSession
{
    private readonly IChatService _chatService;
    private readonly ILogger<ConversationSession> _logger;
    private readonly IToolApprovalService _toolApprovalService;

    /// <summary>Предотвращает одновременный запуск нескольких LLM-тёрнов.</summary>
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private CancellationTokenSource? _commonToolsCancellationTockenSource;

    public Guid ConversationId { get; }
    public Guid CurrentLeafNodeId { get; private set; }
    public ConversationState State { get; private set; }
    //public int LastTotalTokenCount { get; private set; }
    public bool IsRunning { get; private set; }

    public event EventHandler<SessionEvent>? EventOccurred;

    public ConversationSession(
        Guid conversationId,
        IChatService chatService,
        ILogger<ConversationSession> logger,
        IToolApprovalService toolApprovalService)
    {
        _chatService = chatService;
        _logger = logger;
        _toolApprovalService = toolApprovalService;
        ConversationId = conversationId;
    }

    public async Task InitializeAsync(
    CancellationToken ct = default)
    {
        if (!await _runLock.WaitAsync(0, ct)) throw new InvalidOperationException("ConversationSession is already running");
        try
        {
            await InitializeInternalAsync(ct);
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task InitializeInternalAsync(
    CancellationToken ct = default)
    {
        _commonToolsCancellationTockenSource?.Cancel();
        _commonToolsCancellationTockenSource?.Dispose();
        _commonToolsCancellationTockenSource = new CancellationTokenSource();

        var conversation = await _chatService.GetConversationAsync(ConversationId, ct)
            ?? throw new InvalidOperationException($"Conversation {ConversationId} not found");
        CurrentLeafNodeId = conversation.ActiveLeafNodeId
            ?? throw new InvalidOperationException($"Conversation {ConversationId} miss ActiveLeafNodeId");
        var state = await _chatService.GetConversationStateAsync(CurrentLeafNodeId, ct);
        UpdateState(state);

        Emit(new InitializeSessionEvent());
    }

    public async Task SendMessageAsync(string message, Guid? parentNodeId = null, CancellationToken ct = default)
    {
        parentNodeId ??= CurrentLeafNodeId;

        var state = parentNodeId == CurrentLeafNodeId ? State : await _chatService.GetConversationStateAsync((Guid)parentNodeId, ct);
        if (state != ConversationState.LastMessageIsAssistant)
        {
            throw new InvalidOperationException("Last message mast be by assistant or summary");
        }

        if (!await _runLock.WaitAsync(0, ct)) throw new InvalidOperationException("ConversationSession is already running");
        try
        {
            SetRunning(true);

            var userDto = await _chatService.AddUserMessageAsync(
                ConversationId,
                (Guid)parentNodeId,
                message,
                cancellationToken: ct);

            if (parentNodeId != CurrentLeafNodeId) // Id сообщения, к которому добавляется новое в качестве дочернего,
                                                   // не совпадает с id последнего сообщения в диалоге -> произошло ветвление диалога
            {
                await InitializeInternalAsync(ct);
            }
            else                                    // иначе - сообщение добавилось в конец текущей ветки
            {
                CurrentLeafNodeId = userDto.Id;
                Emit(new UserMessageAddedSessionEvent(userDto));

                var newState = await _chatService.GetConversationStateAsync(CurrentLeafNodeId, ct);
                UpdateState(newState);
            }
                

            await RunLlmTurnAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SendMessageAsync for conversation {ConversationId}", ConversationId);
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
        if (State != ConversationState.LastMessageIsUser && State != ConversationState.AllToolCallsCompleted)
        {
            throw new InvalidOperationException("Last message can be resumed, after user message or completed tools block only");
        }
        
        if (!await _runLock.WaitAsync(0, ct)) throw new InvalidOperationException("ConversationSession is already running");
        try
        {
            SetRunning(true);
            await RunLlmTurnAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ResumeAsync for conversation {ConversationId}", ConversationId);
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
        await foreach (var evt in _chatService.GetAssistantResponseAsync(ConversationId, CurrentLeafNodeId, ct))
        {
            switch (evt)
            {
                case AssistantResponseSavedDto saved:
                    CurrentLeafNodeId = saved.LastNodeId;
                    //LastTotalTokenCount = saved.TotalTokenCount;
                    Emit(new StreamSessionEvent(evt));
                    break;
                case ToolCallRequestedDto toolReq:
                    CurrentLeafNodeId = toolReq.PendingNodeId;
                    _ = HandleToolCallRequestedAsync(toolReq, ct);
                    break;
                default:
                    Emit(new StreamSessionEvent(evt));
                    break;
            }
        }

        var state = await _chatService.GetConversationStateAsync(CurrentLeafNodeId, ct);
        UpdateState(state);
    }

    private async Task HandleToolCallRequestedAsync(ToolCallRequestedDto toolReq, CancellationToken ct = default)
    {
        try
        {
            var isAutoapproved = await _toolApprovalService.IsAutoApprovedAsync(toolReq.PluginName, toolReq.FunctionName);

            Emit(new ToolRequestedSessionEvent(
                PendingNodeId: toolReq.PendingNodeId,
                CallId: toolReq.CallId,
                PluginName: toolReq.PluginName,
                FunctionName: toolReq.FunctionName,
                ArgumentsJson: toolReq.ArgumentsJson,
                IsAutoApproved: isAutoapproved));

            if (isAutoapproved)
            {
                await ApproveToolAsync(toolReq.PendingNodeId, ct);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in HandleToolCallRequestedAsync for conversation {ConversationId}", ConversationId);
            Emit(new SessionErrorEvent(ex.Message, ex));
        }
    }

    public async Task ApproveToolAsync(Guid pendingNodeId, CancellationToken ct = default)
    {
        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(ct, _commonToolsCancellationTockenSource!.Token);
        var token = linkedCts.Token;
        var result = await _chatService.ApproveToolCallAsync(pendingNodeId, token);
        await HandleToolCallResultAsync(pendingNodeId, result, token);
    }

    public async Task DenyToolAsync(Guid pendingNodeId, CancellationToken ct = default)
    {
        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(ct, _commonToolsCancellationTockenSource!.Token);
        var token = linkedCts.Token;
        var result = await _chatService.DenyToolCallAsync(pendingNodeId, token);
        await HandleToolCallResultAsync(pendingNodeId, result, token);
    }

    private async Task HandleToolCallResultAsync(Guid pendingNodeId, ToolCallResult toolCallResult, CancellationToken ct = default)
    {
        try
        {
            Emit(new ToolResultSessionEvent(pendingNodeId, toolCallResult.ResultJson, toolCallResult.Status));

            var state = await _chatService.GetConversationStateAsync(CurrentLeafNodeId, ct);
            UpdateState(state);

            if (state != ConversationState.AllToolCallsCompleted) return;

            if (!await _runLock.WaitAsync(0, ct)) throw new InvalidOperationException("ConversationSession is already running");

            try
            {
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
            _logger.LogError(ex, "Error in HandleToolCallResultAsync for conversation {ConversationId}", ConversationId);
            Emit(new SessionErrorEvent(ex.Message, ex));
        }
    }

    public Task<IEnumerable<MessageDto>> LoadHistoryAsync(CancellationToken ct = default)
    {
        return _chatService.GetConversationHistoryAsync(ConversationId, CurrentLeafNodeId, ct);
    }

    public async Task SummarizeAsync(Guid selectedNodeId, CancellationToken ct = default)
    {
        var state = await _chatService.GetConversationStateAsync(selectedNodeId, ct);
        if (state is ConversationState.HasPendingToolCalls)
        {
            var errorMessage = "Cannot summarize: one or more tool actions are still running or waiting for your approval. " +
                           "Wait for them to finish or approve/deny them, then try again.";
            throw new InvalidOperationException(errorMessage);
        }
        if (state is ConversationState.ToolCallIdMismatch)
        {
            var errorMessage = "Cannot summarize at this point: the selected message is in the middle of a tool call sequence. " +
                           "Choose a message that comes before or after the entire tool call group.";
            throw new InvalidOperationException(errorMessage);
        }

        try
        {
            await foreach (var evt in _chatService.SummarizeAsync(ConversationId, selectedNodeId, ct))
            {
                Emit(new SummarizationSessionEvent(evt));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error summarizing message {MessageId}", selectedNodeId);
            Emit(new SessionErrorEvent(ex.Message, ex));
        }
    }

    public async Task SwitchToSiblingAsync(Guid parentNodeId, Guid newChildId, CancellationToken ct = default)
    {
        await _chatService.SwitchToSiblingAsync(
            ConversationId,
            parentNodeId,
            newChildId,
            cancellationToken: ct);

        await InitializeAsync();
    }

    public Task UpdateSystemPromptAsync(string systemPrompt, CancellationToken ct = default)
    {
        return _chatService.UpdateConversationSystemPromptAsync(ConversationId, systemPrompt);
    }

    public Task<ConversationSettingsDto?> GetSettingsAsync(CancellationToken ct = default)
    {
        return _chatService.GetConversationSettingsAsync(ConversationId, ct);
    }

    public Task ChangeProfileAsync(Guid profileId, CancellationToken ct = default)
    {
        return _chatService.ChangeConversationProfileAsync(ConversationId, profileId);
    }

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

    private void Emit(SessionEvent evt) => EventOccurred?.Invoke(this, evt);

    public void Dispose()
    {
        _commonToolsCancellationTockenSource?.Cancel();
        _commonToolsCancellationTockenSource?.Dispose();
        _commonToolsCancellationTockenSource = null;

        _runLock.Dispose();
    }
}
