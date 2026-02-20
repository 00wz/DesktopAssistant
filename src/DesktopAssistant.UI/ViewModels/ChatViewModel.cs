using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Domain.Interfaces;
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
    private readonly IMessageNodeRepository _messageNodeRepository;
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

    public ObservableCollection<ChatMessageModel> Messages { get; } = new();

    public ChatViewModel(
        IChatService chatService,
        IMessageNodeRepository messageNodeRepository,
        ILogger<ChatViewModel> logger)
    {
        _chatService = chatService;
        _messageNodeRepository = messageNodeRepository;
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

        _logger.LogInformation("Created new conversation {ConversationId}", CurrentConversation.Id);
    }

    private async Task LoadMessagesAsync(CancellationToken cancellationToken)
    {
        if (CurrentConversation == null)
            throw new InvalidOperationException("Cannot load messages: CurrentConversation is null");

        var dtos = await _chatService.GetConversationHistoryAsync(CurrentConversation.Id, cancellationToken);

        Messages.Clear();

        foreach (var dto in dtos)
        {
            Messages.Add(ChatMessageModelFactory.FromDto(dto));
        }
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
            userMessage,
            cancellationToken: default);

        Messages.Add(ChatMessageModelFactory.FromDto(userDto));

        IsLoading = true;
        try
        {
            await ProcessAssistantStreamAsync(
                _chatService.GetAssistantResponseAsync(CurrentConversation.Id, default),
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
        !string.IsNullOrWhiteSpace(InputMessage);

    partial void OnInputMessageChanged(string value) => SendMessageCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadingChanged(bool value) => SendMessageCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Очищает чат и создаёт новый диалог
    /// </summary>
    [RelayCommand]
    private async Task NewChatAsync()
    {
        ConversationTitle = "Новый чат";
        await InitializeAsync();
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
    /// Подтверждает выполнение tool-вызова
    /// </summary>
    [RelayCommand]
    private void ApproveTool(ToolChatMessageModel model)
    {
        if (model.Confirmation == null || model.Confirmation.Task.IsCompleted) return;
        model.Status = ToolCallStatus.Approved;
        model.Confirmation.TrySetResult(true);
    }

    /// <summary>
    /// Отклоняет выполнение tool-вызова
    /// </summary>
    [RelayCommand]
    private void DenyTool(ToolChatMessageModel model)
    {
        if (model.Confirmation == null || model.Confirmation.Task.IsCompleted) return;
        model.Status = ToolCallStatus.Denied;
        model.Confirmation.TrySetResult(false);
    }

    /// <summary>
    /// Переходит к предыдущему sibling сообщению
    /// </summary>
    [RelayCommand]
    private async Task NavigateToPreviousSiblingAsync(ChatMessageModel message)
    {
        if (CurrentConversation == null)
            throw new InvalidOperationException("Cannot navigate to previous sibling: CurrentConversation is null");

        if (!message.ParentId.HasValue)
            throw new ArgumentException("Cannot navigate to previous sibling: message.ParentId is null", nameof(message));

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var siblings = await _messageNodeRepository.GetChildrenAsync(
                message.ParentId.Value,
                cancellationToken: default);

            var siblingList = siblings
                .Where(s => s.NodeType == message.NodeType)
                .OrderBy(s => s.CreatedAt)
                .ToList();

            if (siblingList.Count <= 1)
                throw new InvalidOperationException("Cannot navigate to previous sibling: no siblings available");

            var currentIndex = siblingList.FindIndex(s => s.Id == message.Id);
            if (currentIndex <= 0)
                throw new InvalidOperationException("Cannot navigate to previous sibling: already at first sibling");

            var previousSibling = siblingList[currentIndex - 1];

            await _chatService.SwitchToSiblingAsync(
                CurrentConversation.Id,
                message.ParentId.Value,
                previousSibling.Id,
                cancellationToken: default);

            await InitializeAsync(CurrentConversation.Id, default);

            _logger.LogDebug("Navigated to previous sibling {NodeId}", previousSibling.Id);
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
        if (CurrentConversation == null)
            throw new InvalidOperationException("Cannot navigate to next sibling: CurrentConversation is null");

        if (!message.ParentId.HasValue)
            throw new ArgumentException("Cannot navigate to next sibling: message.ParentId is null", nameof(message));

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var siblings = await _messageNodeRepository.GetChildrenAsync(
                message.ParentId.Value,
                cancellationToken: default);

            var siblingList = siblings
                .Where(s => s.NodeType == message.NodeType)
                .OrderBy(s => s.CreatedAt)
                .ToList();

            if (siblingList.Count <= 1)
                throw new InvalidOperationException("Cannot navigate to next sibling: no siblings available");

            var currentIndex = siblingList.FindIndex(s => s.Id == message.Id);
            if (currentIndex < 0 || currentIndex >= siblingList.Count - 1)
                throw new InvalidOperationException("Cannot navigate to next sibling: already at last sibling");

            var nextSibling = siblingList[currentIndex + 1];

            await _chatService.SwitchToSiblingAsync(
                CurrentConversation.Id,
                message.ParentId.Value,
                nextSibling.Id,
                cancellationToken: default);

            await InitializeAsync(CurrentConversation.Id, default);

            _logger.LogDebug("Navigated to next sibling {NodeId}", nextSibling.Id);
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
    /// что позволяет обрабатывать события кнопок (Approve/Deny) пока producer ждёт TCS.
    /// Чанки доставляются как AssistantChunkDto — процессируются на UI thread через SynchronizationContext.
    /// Исключения перехватываются внутри: устанавливается ErrorMessage и выполняется очистка UI.
    /// </summary>
    private async Task ProcessAssistantStreamAsync(
        IAsyncEnumerable<StreamEvent> stream,
        CancellationToken cancellationToken)
    {
        TextChatMessageModel? activeAssistantModel = null;
        var pendingToolModels = new Dictionary<string, ToolChatMessageModel>();

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

                    case ToolCallRequestedDto toolReq:
                        if (activeAssistantModel != null)
                            activeAssistantModel.IsStreaming = false;
                        HandleToolCallRequested(toolReq);
                        break;

                    case ToolCallExecutingDto exec:
                        if (pendingToolModels.TryGetValue(exec.CallId, out var execModel))
                            execModel.Status = ToolCallStatus.Executing;
                        break;

                    case ToolCallCompletedDto done:
                        if (pendingToolModels.TryGetValue(done.CallId, out var doneModel))
                        {
                            doneModel.ResultJson = done.ResultJson;
                            doneModel.Status = ToolCallStatus.Completed;
                            pendingToolModels.Remove(done.CallId);
                        }
                        break;

                    case ToolCallFailedDto fail:
                        if (pendingToolModels.TryGetValue(fail.CallId, out var failModel))
                        {
                            failModel.ErrorMessage = fail.ErrorMessage;
                            failModel.Status = fail.ErrorMessage == "Denied by user"
                                ? ToolCallStatus.Denied
                                : ToolCallStatus.Failed;
                            pendingToolModels.Remove(fail.CallId);
                        }
                        break;

                    case AssistantResponseSavedDto saved:
                        if (activeAssistantModel != null)
                        {
                            activeAssistantModel.IsStreaming = false;
                            activeAssistantModel.Id = saved.LastNodeId;
                        }
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

        void HandleToolCallRequested(ToolCallRequestedDto toolReq)
        {
            var toolModel = new ToolChatMessageModel
            {
                CallId = toolReq.CallId,
                PluginName = toolReq.PluginName,
                FunctionName = toolReq.FunctionName,
                ArgumentsJson = toolReq.ArgumentsJson,
                Status = ToolCallStatus.Pending,
                Confirmation = toolReq.Confirmation
            };

            pendingToolModels[toolReq.CallId] = toolModel;
            Messages.Add(toolModel);

            if (IsAutoApproveTools)
            {
                toolModel.Status = ToolCallStatus.Approved;
                toolReq.Confirmation.TrySetResult(true);
            }
        }

        void Cleanup()
        {
            if (activeAssistantModel?.IsStreaming == true)
            {
                Messages.Remove(activeAssistantModel);
                activeAssistantModel = null;
            }

            foreach (var toolModel in pendingToolModels.Values)
            {
                toolModel.Confirmation?.TrySetCanceled();
                Messages.Remove(toolModel);
            }

            pendingToolModels.Clear();
        }
    }
}
