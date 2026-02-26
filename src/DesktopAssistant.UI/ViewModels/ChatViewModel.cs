using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Entities;
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
    private readonly ILogger<ChatViewModel> _logger;

    [ObservableProperty]
    private Conversation? _currentConversation;

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

    public bool ShowInputPanel => ConversationStatus == ConversationState.LastMessageIsAssistant;
    public bool ShowResumePanel => ConversationStatus == ConversationState.LastMessageIsUser ||
                                   ConversationStatus == ConversationState.AllToolCallsCompleted;

    public ObservableCollection<ChatMessageModel> Messages { get; } = new();

    public ChatViewModel(
        IChatService chatService,
        ILogger<ChatViewModel> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    /// <summary>
    /// Инициализирует новый диалог или загружает существующий
    /// </summary>
    public async Task InitializeAsync(Guid? conversationId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            Messages.Clear();

            if (conversationId.HasValue)
            {
                var conversations = await _chatService.GetConversationsAsync(cancellationToken);
                CurrentConversation = conversations.FirstOrDefault(c => c.Id == conversationId.Value);

                if (CurrentConversation != null)
                {
                    ConversationTitle = CurrentConversation.Title;
                    await LoadMessagesAsync(cancellationToken);
                }
                else
                {
                    _logger.LogWarning("Conversation {ConversationId} not found", conversationId);
                    await CreateNewConversationAsync(cancellationToken);
                }
            }
            else
            {
                await CreateNewConversationAsync(cancellationToken);
            }
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

    private async Task CreateNewConversationAsync(CancellationToken cancellationToken)
    {
        CurrentConversation = await _chatService.CreateConversationAsync(
            ConversationTitle,
            cancellationToken: cancellationToken);

        _currentLeafNodeId = CurrentConversation.ActiveLeafNodeId;
        ConversationStatus = ConversationState.LastMessageIsAssistant;
        _logger.LogInformation("Created new conversation {ConversationId}", CurrentConversation.Id);
    }

    private async Task LoadMessagesAsync(CancellationToken cancellationToken)
    {
        if (CurrentConversation == null)
            throw new InvalidOperationException("Cannot load messages: CurrentConversation is null");

        var dtos = await _chatService.GetConversationHistoryAsync(CurrentConversation.Id, cancellationToken);

        Messages.Clear();

        Guid? lastDtoId = null;
        foreach (var dto in dtos)
        {
            Messages.Add(ChatMessageModelFactory.FromDto(dto));
            lastDtoId = dto.Id;
        }

        _currentLeafNodeId = lastDtoId ?? CurrentConversation.ActiveLeafNodeId;

        if (_currentLeafNodeId.HasValue)
            ConversationStatus = await _chatService.GetConversationStateAsync(_currentLeafNodeId.Value, cancellationToken);
    }

    /// <summary>
    /// Отправляет сообщение AI-ассистенту
    /// </summary>
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

    /// <summary>
    /// Возобновляет диалог: вызывается когда последнее сообщение — пользователя или все tool-вызовы завершены.
    /// </summary>
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

    /// <summary>
    /// Начинает редактирование сообщения пользователя
    /// </summary>
    [RelayCommand]
    private void StartEditMessage(TextChatMessageModel message)
    {
        message.IsEditing = true;
        message.EditedContent = message.Content;
    }

    /// <summary>
    /// Сохраняет отредактированное сообщение и создаёт альтернативную ветвь (sibling)
    /// </summary>
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

            // Создаём sibling — новый узел от того же родителя
            var userDto = await _chatService.AddUserMessageAsync(
                CurrentConversation.Id,
                message.ParentId.Value,
                editedText,
                cancellationToken: default);

            // Перезагружаем историю с новой веткой
            await LoadMessagesAsync(default);

            // Получаем ответ ассистента для новой ветки, начиная с userDto.Id
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

    /// <summary>
    /// Отменяет редактирование сообщения
    /// </summary>
    [RelayCommand]
    private void CancelEditMessage(TextChatMessageModel message)
    {
        message.IsEditing = false;
        message.EditedContent = string.Empty;
    }

    /// <summary>
    /// Подтверждает выполнение tool-вызова.
    /// Вызывает stateless ApproveToolCallAsync по Id (== PendingNodeId в БД).
    /// Если все tools завершены — инициирует следующий тёрн ассистента.
    /// </summary>
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

    /// <summary>
    /// Отклоняет выполнение tool-вызова.
    /// Если все tools завершены — инициирует следующий тёрн ассистента.
    /// </summary>
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

    /// <summary>
    /// Переходит к предыдущему sibling сообщению
    /// </summary>
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

            await InitializeAsync(CurrentConversation.Id, default);
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

    /// <summary>
    /// Переходит к следующему sibling сообщению
    /// </summary>
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

            await InitializeAsync(CurrentConversation.Id, default);
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

    /// <summary>
    /// Обрабатывает IAsyncEnumerable поток событий от LLM.
    /// Создаёт и обновляет UI-модели по мере поступления событий.
    /// await foreach вызывается на UI-потоке: при каждом await MoveNextAsync() UI thread освобождается,
    /// что позволяет обрабатывать другие сообщения пока producer пишет чанки.
    /// Чанки доставляются как AssistantChunkDto — процессируются на UI thread через SynchronizationContext.
    /// Исключения перехватываются внутри: устанавливается ErrorMessage и выполняется очистка UI.
    /// После завершения потока: если IsAutoApproveTools и есть pending tools — авто-подтверждает их.
    /// </summary>
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

            // Авто-подтверждение: запускаем параллельно без ожидания.
            // ApproveToolAsync обрабатывает все исключения внутри.
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
