using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.UI.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel для чата с AI-ассистентом
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private readonly IAssistantProfileService _profileService;
    private readonly ILogger<ChatViewModel> _logger;
    private readonly SemaphoreSlim _sessionEventHandleLock = new(1, 1);

    private IConversationSession? _conversationSession;

    // Используется только при стриминге: текущая модель ассистента, в которую пишутся чанки
    private TextChatMessageModel? _activeAssistantModel;

    [ObservableProperty]
    private string _inputMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInputPanel))]
    [NotifyPropertyChangedFor(nameof(ShowResumePanel))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _conversationTitle = "Новый чат";

    /// <summary>Текущее состояние диалога — управляет доступностью SendMessage и Resume.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    [NotifyPropertyChangedFor(nameof(ShowInputPanel))]
    [NotifyPropertyChangedFor(nameof(ShowResumePanel))]
    private ConversationState _conversationStatus;

    // ── Настройки диалога ────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isSettingsPanelVisible;

    /// <summary>Системный промпт диалога — редактируется пользователем.</summary>
    [ObservableProperty]
    private string _systemPrompt = string.Empty;

    /// <summary>Имя активного профиля ассистента.</summary>
    [ObservableProperty]
    private string _assistantProfileName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<AssistantProfileDto> _availableProfiles = [];

    [ObservableProperty]
    private AssistantProfileDto? _selectedProfile;

    /// <summary>
    /// Статистика токенов последнего ответа
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
        IAssistantProfileService profileService,
        ILogger<ChatViewModel> logger)
    {
        _profileService = profileService;
        _logger = logger;
    }

    // ── Инициализация ────────────────────────────────────────────────────────

    /// <summary>
    /// Привязывается к сессии диалога
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

            await LoadConversationSettingsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing chat");
            ErrorMessage = $"Ошибка инициализации чата: {ex.Message}";
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

    private async Task LoadConversationSettingsAsync(CancellationToken cancellationToken)
    {
        if (_conversationSession == null)
            throw new InvalidOperationException("ConversationSession is null");

        try
        {
            var settings = await _conversationSession.GetSettingsAsync(cancellationToken);
            if (settings == null) return;

            SystemPrompt = settings.SystemPrompt;
            AssistantProfileName = settings.Profile?.Name ?? string.Empty;

            var profiles = await _profileService.GetAssistantProfilesAsync(cancellationToken);
            AvailableProfiles = new ObservableCollection<AssistantProfileDto>(profiles);
            SelectedProfile = settings.AssistantProfileId.HasValue
                ? AvailableProfiles.FirstOrDefault(p => p.Id == settings.AssistantProfileId.Value)
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading conversation settings");
        }
    }

    // ── Обработка событий сессии ─────────────────────────────────────────────

    private void OnSessionEvent(object? sender, SessionEvent evt)
    {
        Dispatcher.UIThread.Post(async () => await HandleSessionEvent(evt));
    }

    private async Task HandleSessionEvent(SessionEvent evt)
    {
        /// _sessionEventHandleLock защищает от коллизии обработки SessionEvent,
        /// например, когда во время обработки InitializeSessionEvent приходит UserMessageAddedSessionEvent 
        await _sessionEventHandleLock.WaitAsync();
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

                case AssistantTurnStartedSessionEvent turn:
                    _activeAssistantModel = new TextChatMessageModel
                    {
                        Id = turn.TempId,
                        NodeType = MessageNodeType.Assistant,
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
                    Messages.Add(new ToolChatMessageModel
                    {
                        Id = toolReq.PendingNodeId,
                        CallId = toolReq.CallId,
                        PluginName = toolReq.PluginName,
                        FunctionName = toolReq.FunctionName,
                        ArgumentsJson = toolReq.ArgumentsJson,
                        Status = toolReq.IsAutoApproved ? ToolCallStatus.Executing : ToolCallStatus.Pending
                    });
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
                    ErrorMessage = $"Ошибка: {error.Message}";
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
        if (_activeAssistantModel?.IsStreaming == true)
        {
            Messages.Remove(_activeAssistantModel);
            _activeAssistantModel = null;
        }
    }

    // ── Настройки диалога ────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleSettingsPanel()
    {
        IsSettingsPanelVisible = !IsSettingsPanelVisible;
    }

    [RelayCommand]
    private async Task SaveSystemPromptAsync()
    {
        if (_conversationSession == null)
            throw new InvalidOperationException("ConversationSession is null");

        try
        {
            await _conversationSession.UpdateSystemPromptAsync(SystemPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving system prompt");
            ErrorMessage = $"Ошибка сохранения системного промпта: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ChangeProfileAsync(AssistantProfileDto profile)
    {
        if (_conversationSession == null)
            throw new InvalidOperationException("ConversationSession is null");

        try
        {
            await _conversationSession.ChangeProfileAsync(profile.Id);
            SelectedProfile = profile;
            AssistantProfileName = profile.Name;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing profile");
            ErrorMessage = $"Ошибка смены профиля: {ex.Message}";
        }
    }

    // ── Отправка сообщений ───────────────────────────────────────────────────

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
            ErrorMessage = $"Ошибка отправки сообщения: {ex.Message}";
        }
    }

    private bool CanSendMessage() =>
        !IsLoading &&
        _conversationSession != null &&
        !string.IsNullOrWhiteSpace(InputMessage) &&
        ConversationStatus == ConversationState.LastMessageIsAssistant;

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
            ErrorMessage = $"Ошибка возобновления диалога: {ex.Message}";
        }
    }

    // ── Редактирование сообщений ─────────────────────────────────────────────

    [RelayCommand]
    private void StartEditMessage(TextChatMessageModel message)
    {
        message.IsEditing = true;
        message.EditedContent = message.Content;
    }

    [RelayCommand]
    private async Task SaveEditedMessageAsync(TextChatMessageModel message)
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
            ErrorMessage = $"Ошибка сохранения сообщения: {ex.Message}";
            message.IsEditing = false;
        }
    }

    [RelayCommand]
    private void CancelEditMessage(TextChatMessageModel message)
    {
        message.IsEditing = false;
        message.EditedContent = string.Empty;
    }

    [RelayCommand]
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

    [RelayCommand]
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

    // ── Суммаризация ─────────────────────────────────────────────────────────

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
            ErrorMessage = $"Ошибка суммаризации: {ex.Message}";
        }
    }

    // ── Навигация по siblings ─────────────────────────────────────────────────

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
            ErrorMessage = $"Ошибка навигации: {ex.Message}";
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
            ErrorMessage = $"Ошибка навигации: {ex.Message}";
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
