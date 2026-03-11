using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.Infrastructure.AI;

internal class ConversationSession : IConversationSession
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConversationSession> _logger;
    private readonly IToolApprovalService _toolApprovalService;

    /// <summary>Предотвращает одновременный запуск нескольких LLM-тёрнов.</summary>
    private readonly SemaphoreSlim _runLock = new(1, 1);

    /// <summary>
    /// Отменяет все активные операции Approve/Deny при переинициализации сессии
    /// (например, при переключении на другую ветку диалога).
    /// </summary>
    private CancellationTokenSource _toolsCancellationTokenSource = new();

    public Guid ConversationId { get; }
    public Guid CurrentLeafNodeId { get; private set; }
    public ConversationState State { get; private set; }
    public bool IsRunning { get; private set; }

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

    // ── Инициализация ────────────────────────────────────────────────────────

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
        // Отменяем все незавершённые Approve/Deny операции из предыдущего состояния
        _toolsCancellationTokenSource.Cancel();
        _toolsCancellationTokenSource.Dispose();
        _toolsCancellationTokenSource = new CancellationTokenSource();

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

    // ── LLM-операции ─────────────────────────────────────────────────────────

    public async Task SendMessageAsync(string message, Guid? parentNodeId = null, CancellationToken ct = default)
    {
        var targetParentId = parentNodeId ?? CurrentLeafNodeId;

        // Если parentNodeId отличается от текущего листа — проверяем состояние той ветки отдельно
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

        if (!await _runLock.WaitAsync(0, ct))
            throw new InvalidOperationException("Cannot send message: another LLM turn is already in progress.");
        try
        {
            SetRunning(true);

            using var scope = _scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

            var userDto = await chatService.AddUserMessageAsync(
                ConversationId,
                targetParentId,
                message,
                cancellationToken: ct);

            if (targetParentId != CurrentLeafNodeId)
            {
                // Добавление к не-текущей ветке (редактирование сообщения) → переинициализируем сессию
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
        if (State != ConversationState.LastMessageIsUser && State != ConversationState.AllToolCallsCompleted)
            throw new InvalidOperationException(
                $"Cannot resume: expected state {ConversationState.LastMessageIsUser} or " +
                $"{ConversationState.AllToolCallsCompleted}, but got {State}.");

        if (!await _runLock.WaitAsync(0, ct))
            throw new InvalidOperationException("Cannot resume: another LLM turn is already in progress.");
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

        await foreach (var evt in chatService.GetAssistantResponseAsync(ConversationId, CurrentLeafNodeId, ct))
        {
            switch (evt)
            {
                case AssistantResponseSavedDto saved:
                    CurrentLeafNodeId = saved.LastNodeId;
                    Emit(new StreamSessionEvent(evt));
                    break;
                case ToolCallRequestedDto toolReq:
                    CurrentLeafNodeId = toolReq.PendingNodeId;
                    // fire-and-forget: каждый tool-вызов обрабатывается независимо (возможно параллельно)
                    _ = HandleToolCallRequestedAsync(toolReq, ct);
                    break;
                default:
                    Emit(new StreamSessionEvent(evt));
                    break;
            }
        }

        // Обновляем состояние после завершения стриминга.
        // Если были tool-вызовы, состояние может ещё меняться асинхронно через HandleToolCallResultAsync.
        using var stateScope = _scopeFactory.CreateScope();
        var stateChatService = stateScope.ServiceProvider.GetRequiredService<IChatService>();
        var state = await stateChatService.GetConversationStateAsync(CurrentLeafNodeId, ct);
        UpdateState(state);
    }

    /// <summary>
    /// Обрабатывает входящий tool-вызов: определяет нужно ли автоподтверждение,
    /// публикует <see cref="ToolRequestedSessionEvent"/> и при необходимости автоматически одобряет.
    /// Вызывается через fire-and-forget — все исключения обрабатываются внутри.
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
            // Перехватываем все исключения (включая OperationCanceledException),
            // так как метод вызывается через fire-and-forget — необработанное исключение
            // привело бы к UnobservedTaskException.
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

        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
        var result = await chatService.ApproveToolCallAsync(pendingNodeId, linkedCts.Token);
        await HandleToolCallResultAsync(pendingNodeId, result, linkedCts.Token);
    }

    public async Task DenyToolAsync(Guid pendingNodeId, CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct, _toolsCancellationTokenSource.Token);

        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
        var result = await chatService.DenyToolCallAsync(pendingNodeId, linkedCts.Token);
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

            // При параллельном завершении нескольких tools только первый запускает следующий тёрн;
            // остальные просто выходят.
            if (!await _runLock.WaitAsync(0, ct)) return;

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
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

        var state = await chatService.GetConversationStateAsync(selectedNodeId, ct);
        if (state is ConversationState.HasPendingToolCalls)
            throw new InvalidOperationException(
                "Cannot summarize: one or more tool calls are still pending approval. " +
                "Approve or deny them first, then try again.");
        if (state is ConversationState.ToolCallIdMismatch)
            throw new InvalidOperationException(
                "Cannot summarize at this point: the selected message is in the middle of a tool call sequence. " +
                "Select a message before or after the entire tool call group.");

        try
        {
            await foreach (var evt in chatService.SummarizeAsync(ConversationId, selectedNodeId, ct))
                Emit(new SummarizationSessionEvent(evt));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error summarizing node {NodeId} in conversation {ConversationId}",
                selectedNodeId, ConversationId);
            Emit(new SessionErrorEvent(ex.Message, ex));
        }
    }

    public async Task SwitchToSiblingAsync(Guid parentNodeId, Guid newChildId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
        await chatService.SwitchToSiblingAsync(ConversationId, parentNodeId, newChildId, cancellationToken: ct);
        await InitializeAsync(ct);
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

    // ── Вспомогательные методы ───────────────────────────────────────────────

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
        _toolsCancellationTokenSource.Cancel();
        _toolsCancellationTokenSource.Dispose();
        _runLock.Dispose();
    }
}
