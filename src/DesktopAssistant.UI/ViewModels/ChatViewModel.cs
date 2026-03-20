using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.UI.Models;
using Microsoft.Extensions.Logging;
using OpenAI.Realtime;
using System.Collections.ObjectModel;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel for the AI assistant chat
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private readonly ILogger<ChatViewModel> _logger;
    private readonly SemaphoreSlim _sessionEventHandleLock = new(1, 1);
    private const int SessionEventLockTimeoutMs = 5000;

    private IConversationSession? _conversationSession;

    // Used only during streaming: the current assistant model that receives chunks
    private AssistantChatMessageModel? _activeAssistantModel;

    [ObservableProperty]
    private string _inputMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInputPanel))]
    [NotifyPropertyChangedFor(nameof(ShowResumePanel))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Current conversation state — controls availability of SendMessage and Resume.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    [NotifyPropertyChangedFor(nameof(ShowInputPanel))]
    [NotifyPropertyChangedFor(nameof(ShowResumePanel))]
    private ConversationState _conversationStatus;

    // ── Conversation settings ────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isSettingsPanelVisible;

    /// <summary>Name of the active assistant profile — displayed in the chat header.</summary>
    [ObservableProperty]
    private string _assistantProfileName = string.Empty;

    public ChatSettingsPanelViewModel Settings { get; }

    /// <summary>
    /// Token statistics for the last response
    /// </summary>
    [ObservableProperty]
    private int _lastTotalTokenCount;

    public bool ShowResumePanel => !IsLoading &&
                                   (ConversationStatus == ConversationState.LastMessageIsUser ||
                                    ConversationStatus == ConversationState.AllToolCallsCompleted);
    public bool ShowInputPanel => !ShowResumePanel;

    public Guid? ConversationId => _conversationSession?.ConversationId;

    public ObservableCollection<ChatMessageModel> Messages { get; } = new();

    public ChatViewModel(
        ChatSettingsPanelViewModel settings,
        ILogger<ChatViewModel> logger)
    {
        Settings = settings;
        Settings.OnProfileApplied = name => AssistantProfileName = name;
        _logger = logger;
    }

    // ── Initialization ───────────────────────────────────────────────────────

    /// <summary>
    /// Binds to a conversation session
    /// </summary>
    public async Task InitializeAsync(
        IConversationSession conversationSession,
        CancellationToken cancellationToken = default)
    {
        if (conversationSession == null)
            throw new InvalidOperationException("ConversationSession is null");

        try
        {
            UnsubscribeCurrentSession();
            _conversationSession = conversationSession;
            IsLoading = true;
            ErrorMessage = null;
            Messages.Clear();

            await LoadMessagesAsync(cancellationToken);
            ConversationStatus = _conversationSession.State;
            _conversationSession.EventOccurred += OnSessionEvent;

            var settings = await _conversationSession.GetSettingsAsync(cancellationToken);
            AssistantProfileName = settings?.Profile?.ModelId ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing chat");
            ErrorMessage = $"Error initializing chat: {ex.Message}";
        }
        finally
        {
            IsLoading = _conversationSession?.IsRunning ?? false;
        }
    }

    private async Task LoadMessagesAsync(CancellationToken cancellationToken)
    {
        if (_conversationSession == null)
            throw new InvalidOperationException("ConversationSession is null");

        var dtos = await _conversationSession.LoadHistoryAsync(cancellationToken);

        Messages.Clear();
        _activeAssistantModel = null;

        AssistantMessageDto? lastAssistantDto = null;
        foreach (var dto in dtos)
        {
            Messages.Add(ChatMessageModelFactory.FromDto(dto));
            if (dto is AssistantMessageDto assistantDto)
                lastAssistantDto = assistantDto;
        }

        if (lastAssistantDto != null)
        {
            LastTotalTokenCount = lastAssistantDto.TotalTokenCount;
        }
    }


    // ── Session event handling ───────────────────────────────────────────────

    private void OnSessionEvent(object? sender, SessionEvent evt)
    {
        Dispatcher.UIThread.Post(async () => await HandleSessionEvent(evt));
    }

    private async Task HandleSessionEvent(SessionEvent evt)
    {
        // Guard against collision when events arrive concurrently (e.g. UserMessageAdded during
        // an InitializeSession reload). Timeout prevents a deadlock if a handler throws.
        if (!await _sessionEventHandleLock.WaitAsync(SessionEventLockTimeoutMs))
        {
            _logger.LogWarning("Session event handler lock timed out for {EventType}", evt.GetType().Name);
            return;
        }
        try
        {
            switch (evt)
            {
                case RunningStateChangedSessionEvent running:
                    IsLoading = running.IsRunning;
                    break;

                case ConversationStateChangedSessionEvent state:
                    ConversationStatus = state.State;
                    break;

                case UserMessageAddedSessionEvent userEvt:
                    Messages.Add(ChatMessageModelFactory.FromDto(userEvt.Dto));
                    break;

                // TODO: Implement a mechanism where the Assistant message will be displayed correctly,
                // even if the subscription to _conversationSession.EventOccurred occurred after
                // the AssistantTurnStartedSessionEvent appeared.
                case AssistantTurnStartedSessionEvent turn:
                    _activeAssistantModel = new AssistantChatMessageModel
                    {
                        Id = turn.TempId,
                        CreatedAt = turn.StartedAt,
                        IsStreaming = true
                    };
                    Messages.Add(_activeAssistantModel);
                    break;

                case AssistantChunkSessionEvent chunk:
                    _activeAssistantModel?.AppendContent(chunk.Text);
                    break;

                case AssistantResponseSavedSessionEvent saved:
                    if (_activeAssistantModel != null)
                    {
                        _activeAssistantModel.IsStreaming = false;
                        _activeAssistantModel.Id = saved.LastNodeId;
                        _activeAssistantModel = null;
                    }
                    LastTotalTokenCount = saved.TotalTokenCount;
                    break;

                case ToolRequestedSessionEvent toolReq:
                    var toolStatus = toolReq.IsAutoApproved ? ToolCallStatus.Executing : ToolCallStatus.Pending;
                    ToolChatMessageModel toolCallModel = toolReq.IsTerminal
                        ? new AgentResultModel
                        {
                            Id = toolReq.PendingNodeId,
                            CallId = toolReq.CallId,
                            PluginName = toolReq.PluginName,
                            FunctionName = toolReq.FunctionName,
                            Status = toolStatus,
                            Message = AgentResultModel.ExtractMessage(toolReq.ArgumentsJson)
                        }
                        : new RegularToolCallModel
                        {
                            Id = toolReq.PendingNodeId,
                            CallId = toolReq.CallId,
                            PluginName = toolReq.PluginName,
                            FunctionName = toolReq.FunctionName,
                            ArgumentsJson = toolReq.ArgumentsJson,
                            Status = toolStatus
                        };
                    Messages.Add(toolCallModel);
                    break;

                case ToolResultSessionEvent toolRes:
                    var toolModel = Messages.OfType<ToolChatMessageModel>()
                        .FirstOrDefault(m => m.Id == toolRes.PendingNodeId);
                    if (toolModel != null)
                    {
                        toolModel.Status = toolRes.Status == ToolNodeStatus.Failed
                            ? ToolCallStatus.Failed
                            : toolRes.Status == ToolNodeStatus.Denied
                                ? ToolCallStatus.Denied
                                : ToolCallStatus.Completed;
                        toolModel.ResultJson = toolRes.ResultJson;
                    }
                    break;

                case SessionErrorEvent error:
                    ErrorMessage = $"Error: {error.Message}";
                    CleanupStreamingModel();
                    break;

                case SummarizationStartedSessionEvent started:
                {
                    var summaryModel = new SummarizationChatMessageModel { Status = SummarizationStatus.Running };
                    var parent = Messages.FirstOrDefault(m => m.Id == started.ParentNodeId);
                    var parentIndex = parent != null ? Messages.IndexOf(parent) : -1;
                    if (parentIndex >= 0)
                        Messages.Insert(parentIndex + 1, summaryModel);
                    break;
                }

                case SummarizationCompletedSessionEvent completed:
                {
                    var parent = Messages.FirstOrDefault(m => m.Id == completed.ParentNodeId);
                    var parentIndex = parent != null ? Messages.IndexOf(parent) : -1;
                    if (parentIndex < 0) break;
                    var next = Messages.ElementAtOrDefault(parentIndex + 1);
                    if (next is SummarizationChatMessageModel existing)
                    {
                        existing.Id = completed.SummaryNodeId;
                        existing.SummaryContent = completed.SummaryContent;
                        existing.Status = SummarizationStatus.Completed;
                    }
                    else
                    {
                        Messages.Insert(parentIndex + 1, new SummarizationChatMessageModel
                        {
                            Id = completed.SummaryNodeId,
                            SummaryContent = completed.SummaryContent,
                            Status = SummarizationStatus.Completed
                        });
                    }
                    break;
                }

                case InitializeSessionEvent:
                    await LoadMessagesAsync(default);
                    break;
            }
        }
        finally
        {
            _sessionEventHandleLock.Release();
        }
    }
    private void CleanupStreamingModel()
    {
        if (_activeAssistantModel is { IsStreaming: true })
        {
            Messages.Remove(_activeAssistantModel);
            _activeAssistantModel = null;
        }
    }

    // ── Conversation settings ─────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleSettingsPanelAsync()
    {
        IsSettingsPanelVisible = !IsSettingsPanelVisible;

        if (IsSettingsPanelVisible && _conversationSession != null)
            await Settings.LoadAsync(_conversationSession);
    }

    // ── Sending messages ─────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (_conversationSession == null)
            throw new InvalidOperationException("ConversationSession is null");

        if (string.IsNullOrWhiteSpace(InputMessage))
            throw new InvalidOperationException("Cannot send message: InputMessage is empty");

        var userMessage = InputMessage.Trim();
        InputMessage = string.Empty;
        ErrorMessage = null;

        try
        {
            await _conversationSession.SendMessageAsync(userMessage);
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Error sending message");
            ErrorMessage = $"Error sending message: {ex.Message}";
        }
    }

    private bool CanSendMessage() =>
        !IsLoading &&
        _conversationSession != null &&
        !string.IsNullOrWhiteSpace(InputMessage) &&
        (ConversationStatus == ConversationState.LastMessageIsAssistant ||
         ConversationStatus == ConversationState.AgentTaskCompleted);

    private bool CanResume() =>
        !IsLoading &&
        _conversationSession != null &&
        (ConversationStatus == ConversationState.LastMessageIsUser ||
         ConversationStatus == ConversationState.AllToolCallsCompleted);

    partial void OnInputMessageChanged(string value) => SendMessageCommand.NotifyCanExecuteChanged();

    partial void OnIsLoadingChanged(bool value)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task ResumeAsync()
    {
        if (_conversationSession == null)
            throw new InvalidOperationException("ConversationSession is null");

        ErrorMessage = null;

        try
        {
            await _conversationSession.ResumeAsync();
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Error resuming dialogue");
            ErrorMessage = $"Error resuming conversation: {ex.Message}";
        }
    }

    // ── Message editing ──────────────────────────────────────────────────────

    [RelayCommand]
    private void StartEditMessage(UserChatMessageModel message)
    {
        message.IsEditing = true;
        message.EditedContent = message.Content;
    }

    [RelayCommand]
    private async Task SaveEditedMessageAsync(UserChatMessageModel message)
    {
        if (_conversationSession == null)
            throw new InvalidOperationException("ConversationSession is null");

        if (!message.ParentId.HasValue)
            throw new ArgumentException("Cannot save edited message: message.ParentId is null", nameof(message));

        try
        {
            ErrorMessage = null;

            var editedText = message.EditedContent.Trim();

            await _conversationSession.SendMessageAsync(editedText, message.ParentId.Value);

            message.IsEditing = false;

            _logger.LogInformation("Created sibling message from edited message {MessageId}", message.ParentId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving edited message");
            ErrorMessage = $"Error saving message: {ex.Message}";
            message.IsEditing = false;
        }
    }

    [RelayCommand]
    private void CancelEditMessage(UserChatMessageModel message)
    {
        message.IsEditing = false;
        message.EditedContent = string.Empty;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ApproveToolAsync(ToolChatMessageModel model)
    {
        if (_conversationSession == null)
            throw new InvalidOperationException("ConversationSession is null");

        if (model.Status != ToolCallStatus.Pending) return;

        model.Status = ToolCallStatus.Executing;

        try
        {
            await _conversationSession.ApproveToolAsync(model.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving tool call {NodeId}", model.Id);
            model.Status = ToolCallStatus.Pending;
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task DenyToolAsync(ToolChatMessageModel model)
    {
        if (_conversationSession == null)
            throw new InvalidOperationException("ConversationSession is null");

        if (model.Status != ToolCallStatus.Pending) return;

        model.Status = ToolCallStatus.Executing;

        try
        {
            await _conversationSession.DenyToolAsync(model.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error denying tool call {NodeId}", model.Id);
            model.Status = ToolCallStatus.Pending;
            ErrorMessage = ex.Message;
        }
    }

    // ── Summarization ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SummarizeMessageAsync(ChatMessageModel message)
    {
        if (_conversationSession == null)
            throw new InvalidOperationException("ConversationSession is null");

        ErrorMessage = null;

        try
        {
            await _conversationSession.SummarizeAsync(message.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error summarizing message {MessageId}", message.Id);
            ErrorMessage = $"Summarization error: {ex.Message}";
        }
    }

    // ── Sibling navigation ───────────────────────────────────────────────────

    [RelayCommand]
    private async Task NavigateToPreviousSiblingAsync(ChatMessageModel message)
    {
        if (_conversationSession == null)
            throw new InvalidOperationException("ConversationSession is null");

        if (!message.ParentId.HasValue || !message.PreviousSiblingId.HasValue)
            return;

        try
        {
            ErrorMessage = null;

            await _conversationSession.SwitchToSiblingAsync(message.ParentId.Value, message.PreviousSiblingId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to previous sibling");
            ErrorMessage = $"Navigation error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task NavigateToNextSiblingAsync(ChatMessageModel message)
    {
        if (_conversationSession == null)
            throw new InvalidOperationException("ConversationSession is null");

        if (!message.ParentId.HasValue || !message.NextSiblingId.HasValue)
            return;

        try
        {
            ErrorMessage = null;

            await _conversationSession.SwitchToSiblingAsync(message.ParentId.Value, message.NextSiblingId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to next sibling");
            ErrorMessage = $"Navigation error: {ex.Message}";
        }
    }

    private void UnsubscribeCurrentSession()
    {
        if (_conversationSession != null)
            _conversationSession.EventOccurred -= OnSessionEvent;
        _conversationSession = null;
        _activeAssistantModel = null;
    }
}
