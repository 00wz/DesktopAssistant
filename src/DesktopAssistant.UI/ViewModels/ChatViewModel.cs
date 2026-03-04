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
    private readonly IChatService _chatService;
    private readonly IAssistantProfileService _profileService;
    private readonly ILogger<ChatViewModel> _logger;

    [ObservableProperty]
    private ConversationDto? _currentConversation;

    [ObservableProperty]
    private string _inputMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _conversationTitle = "Новый чат";

    /// <summary>Если true — tool-вызовы выполняются без запроса подтверждения.</summary>
    [ObservableProperty]
    private bool _isAutoApproveTools;

    /// <summary>ID последнего узла активной ветки — передаётся явно во все методы сервиса.</summary>
    private Guid? _currentLeafNodeId;

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

    public bool ShowInputPanel => ConversationStatus == ConversationState.LastMessageIsAssistant;
    public bool ShowResumePanel => ConversationStatus == ConversationState.LastMessageIsUser ||
                                   ConversationStatus == ConversationState.AllToolCallsCompleted;

    public ObservableCollection<ChatMessageModel> Messages { get; } = new();

    public ChatViewModel(
        IChatService chatService,
        IAssistantProfileService profileService,
        ILogger<ChatViewModel> logger)
    {
        _chatService = chatService;
        _profileService = profileService;
        _logger = logger;
    }

    /// <summary>
    /// Загружает существующий диалог по ID.
    /// Создание нового диалога — ответственность вызывающей стороны.
    /// </summary>
    public async Task InitializeAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            Messages.Clear();

            CurrentConversation = await _chatService.GetConversationAsync(conversationId, cancellationToken)
                ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

            ConversationTitle = CurrentConversation.Title;
            await LoadMessagesAsync(cancellationToken);
            await LoadConversationSettingsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing chat");
            ErrorMessage = $"Ошибка инициализации чата: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadMessagesAsync(CancellationToken cancellationToken)
    {
        if (CurrentConversation == null)
            throw new InvalidOperationException("Cannot load messages: CurrentConversation is null");

        var dtos = await _chatService.GetConversationHistoryAsync(CurrentConversation.Id, cancellationToken);

        Messages.Clear();

        Guid? lastDtoId = null;
        AssistantMessageDto? lastAssistantDto = null;
        foreach (var dto in dtos)
        {
            Messages.Add(ChatMessageModelFactory.FromDto(dto));
            lastDtoId = dto.Id;
            if (dto is AssistantMessageDto assistantDto)
                lastAssistantDto = assistantDto;
        }

        if (lastAssistantDto != null)
        {
            LastTotalTokenCount = lastAssistantDto.TotalTokenCount;
        }

        _currentLeafNodeId = lastDtoId ?? CurrentConversation.ActiveLeafNodeId;

        if (_currentLeafNodeId.HasValue)
            ConversationStatus = await _chatService.GetConversationStateAsync(_currentLeafNodeId.Value, cancellationToken);
    }

    private async Task LoadConversationSettingsAsync(CancellationToken cancellationToken)
    {
        if (CurrentConversation == null) return;

        try
        {
            var settings = await _chatService.GetConversationSettingsAsync(CurrentConversation.Id, cancellationToken);
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

    // ── Настройки диалога ────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleSettingsPanel()
    {
        IsSettingsPanelVisible = !IsSettingsPanelVisible;
    }

    [RelayCommand]
    private async Task SaveSystemPromptAsync()
    {
        if (CurrentConversation == null) return;

        try
        {
            await _chatService.UpdateConversationSystemPromptAsync(CurrentConversation.Id, SystemPrompt);
            _logger.LogInformation("System prompt updated for conversation {ConversationId}", CurrentConversation.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving system prompt");
            ErrorMessage = $"Ошибка сохранения системного промпта: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ChangeProfileAsync(AssistantProfileDto? profile)
    {
        if (CurrentConversation == null || profile == null) return;

        try
        {
            await _chatService.ChangeConversationProfileAsync(CurrentConversation.Id, profile.Id);
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
        if (CurrentConversation == null)
            throw new InvalidOperationException("Cannot send message: CurrentConversation is null");

        if (string.IsNullOrWhiteSpace(InputMessage))
            throw new InvalidOperationException("Cannot send message: InputMessage is empty");

        var userMessage = InputMessage.Trim();
        InputMessage = string.Empty;
        ErrorMessage = null;

        var userDto = await _chatService.AddUserMessageAsync(
            CurrentConversation.Id,
            _currentLeafNodeId!.Value,
            userMessage,
            cancellationToken: default);

        _currentLeafNodeId = userDto.Id;
        Messages.Add(ChatMessageModelFactory.FromDto(userDto));

        IsLoading = true;
        try
        {
            await ProcessAssistantStreamAsync(
                _chatService.GetAssistantResponseAsync(CurrentConversation.Id, userDto.Id, default),
                default);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanSendMessage() =>
        !IsLoading &&
        CurrentConversation != null &&
        !string.IsNullOrWhiteSpace(InputMessage) &&
        ConversationStatus == ConversationState.LastMessageIsAssistant;

    private bool CanResume() =>
        !IsLoading &&
        CurrentConversation != null &&
        _currentLeafNodeId.HasValue &&
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
        if (CurrentConversation == null || !_currentLeafNodeId.HasValue) return;

        IsLoading = true;
        try
        {
            await ProcessAssistantStreamAsync(
                _chatService.GetAssistantResponseAsync(CurrentConversation.Id, _currentLeafNodeId.Value, default),
                default);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void StartEditMessage(TextChatMessageModel message)
    {
        message.IsEditing = true;
        message.EditedContent = message.Content;
    }

    [RelayCommand]
    private async Task SaveEditedMessageAsync(TextChatMessageModel message)
    {
        if (CurrentConversation == null)
            throw new InvalidOperationException("Cannot save edited message: CurrentConversation is null");

        if (!message.ParentId.HasValue)
            throw new ArgumentException("Cannot save edited message: message.ParentId is null", nameof(message));

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var editedText = message.EditedContent.Trim();

            var userDto = await _chatService.AddUserMessageAsync(
                CurrentConversation.Id,
                message.ParentId.Value,
                editedText,
                cancellationToken: default);

            await LoadMessagesAsync(default);

            await ProcessAssistantStreamAsync(
                _chatService.GetAssistantResponseAsync(CurrentConversation.Id, userDto.Id, default),
                default);

            message.IsEditing = false;

            _logger.LogInformation("Created sibling message {MessageId} from edited message", userDto.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving edited message");
            ErrorMessage = $"Ошибка сохранения сообщения: {ex.Message}";
            message.IsEditing = false;
        }
        finally
        {
            IsLoading = false;
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
        if (model.Status != ToolCallStatus.Pending) return;

        model.Status = ToolCallStatus.Executing;

        try
        {
            var result = await _chatService.ApproveToolCallAsync(model.Id);

            model.Status = result.IsError ? ToolCallStatus.Failed : ToolCallStatus.Completed;
            model.ResultJson = result.ResultJson;
            if (result.ErrorMessage != null)
                model.ErrorMessage = result.ErrorMessage;

            var state = await _chatService.GetConversationStateAsync(_currentLeafNodeId!.Value);
            ConversationStatus = state;

            if (state == ConversationState.AllToolCallsCompleted && CurrentConversation != null)
            {
                IsLoading = true;
                await ProcessAssistantStreamAsync(
                    _chatService.GetAssistantResponseAsync(CurrentConversation.Id, _currentLeafNodeId!.Value, default),
                    default);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving tool call {NodeId}", model.Id);
            model.Status = ToolCallStatus.Failed;
            model.ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DenyToolAsync(ToolChatMessageModel model)
    {
        if (model.Status != ToolCallStatus.Pending) return;

        model.Status = ToolCallStatus.Executing;

        try
        {
            await _chatService.DenyToolCallAsync(model.Id);

            model.Status = ToolCallStatus.Denied;

            var state = await _chatService.GetConversationStateAsync(_currentLeafNodeId!.Value);
            ConversationStatus = state;

            if (state == ConversationState.AllToolCallsCompleted && CurrentConversation != null)
            {
                IsLoading = true;
                await ProcessAssistantStreamAsync(
                    _chatService.GetAssistantResponseAsync(CurrentConversation.Id, _currentLeafNodeId!.Value, default),
                    default);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error denying tool call {NodeId}", model.Id);
            model.Status = ToolCallStatus.Failed;
            model.ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToPreviousSiblingAsync(ChatMessageModel message)
    {
        if (CurrentConversation == null || !message.ParentId.HasValue || !message.PreviousSiblingId.HasValue)
            return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            await _chatService.SwitchToSiblingAsync(
                CurrentConversation.Id,
                message.ParentId.Value,
                message.PreviousSiblingId.Value,
                cancellationToken: default);

            await InitializeAsync(CurrentConversation.Id, cancellationToken: default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to previous sibling");
            ErrorMessage = $"Ошибка навигации: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToNextSiblingAsync(ChatMessageModel message)
    {
        if (CurrentConversation == null || !message.ParentId.HasValue || !message.NextSiblingId.HasValue)
            return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            await _chatService.SwitchToSiblingAsync(
                CurrentConversation.Id,
                message.ParentId.Value,
                message.NextSiblingId.Value,
                cancellationToken: default);

            await InitializeAsync(CurrentConversation.Id, cancellationToken: default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to next sibling");
            ErrorMessage = $"Ошибка навигации: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ProcessAssistantStreamAsync(
        IAsyncEnumerable<StreamEvent> stream,
        CancellationToken cancellationToken)
    {
        TextChatMessageModel? activeAssistantModel = null;

        try
        {
            await foreach (var evt in stream.WithCancellation(cancellationToken))
            {
                switch (evt)
                {
                    case AssistantTurnDto turn:
                        var model = new TextChatMessageModel
                        {
                            Id = turn.TempId,
                            NodeType = MessageNodeType.Assistant,
                            CreatedAt = turn.StartedAt,
                            IsStreaming = true
                        };
                        activeAssistantModel = model;
                        Messages.Add(model);
                        break;

                    case AssistantChunkDto chunk:
                        activeAssistantModel?.AppendContent(chunk.Text);
                        break;

                    case AssistantResponseSavedDto saved:
                        if (activeAssistantModel != null)
                        {
                            activeAssistantModel.IsStreaming = false;
                            activeAssistantModel.Id = saved.LastNodeId;
                        }
                        _currentLeafNodeId = saved.LastNodeId;
                        LastTotalTokenCount = saved.TotalTokenCount;
                        break;

                    case ToolCallRequestedDto toolReq:
                        _currentLeafNodeId = toolReq.PendingNodeId;
                        HandleToolCallRequested(toolReq);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing assistant stream");
            ErrorMessage = $"Ошибка получения ответа ассистента: {ex.Message}";
            Cleanup();
        }

        if (_currentLeafNodeId.HasValue)
            ConversationStatus = await _chatService.GetConversationStateAsync(_currentLeafNodeId.Value, CancellationToken.None);

        void HandleToolCallRequested(ToolCallRequestedDto toolReq)
        {
            var toolModel = new ToolChatMessageModel
            {
                Id = toolReq.PendingNodeId,
                CallId = toolReq.CallId,
                PluginName = toolReq.PluginName,
                FunctionName = toolReq.FunctionName,
                ArgumentsJson = toolReq.ArgumentsJson,
                Status = ToolCallStatus.Pending
            };

            Messages.Add(toolModel);

            if (IsAutoApproveTools)
                _ = ApproveToolAsync(toolModel);
        }

        void Cleanup()
        {
            if (activeAssistantModel?.IsStreaming == true)
            {
                Messages.Remove(activeAssistantModel);
                activeAssistantModel = null;
            }
        }
    }
}
