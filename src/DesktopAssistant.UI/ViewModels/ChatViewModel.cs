using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Application.Services;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Domain.Interfaces;
using DesktopAssistant.UI.Models;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel для чата с AI-ассистентом
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private readonly IChatService _chatService;
    private readonly ConversationService _conversationService;
    private readonly IMessageNodeRepository _messageNodeRepository;
    private readonly IConversationBranchRepository _branchRepository;
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

    public ObservableCollection<ChatMessageModel> Messages { get; } = new();

    public ChatViewModel(
        IChatService chatService,
        ConversationService conversationService,
        IMessageNodeRepository messageNodeRepository,
        IConversationBranchRepository branchRepository,
        ILogger<ChatViewModel> logger)
    {
        _chatService = chatService;
        _conversationService = conversationService;
        _messageNodeRepository = messageNodeRepository;
        _branchRepository = branchRepository;
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
                // Загружаем существующий диалог
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
        if (CurrentConversation == null) return;

        var messages = await _chatService.GetConversationHistoryAsync(CurrentConversation.Id, cancellationToken);

        Messages.Clear();

        foreach (var message in messages)
        {
            // Не показываем системные сообщения в UI
            if (message.NodeType != MessageNodeType.System)
            {
                var chatMessage = new ChatMessageModel(
                    message.Id,
                    message.NodeType,
                    message.Content,
                    message.CreatedAt)
                {
                    ParentId = message.ParentId  // Important: preserve ParentId
                };

                Messages.Add(chatMessage);
            }
        }

        // Update sibling information for all messages
        foreach (var message in Messages.Where(m => m.ParentId.HasValue))
        {
            await UpdateSiblingInfoAsync(message, cancellationToken);
        }
    }

    /// <summary>
    /// Отправляет сообщение AI-ассистенту
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (CurrentConversation == null || string.IsNullOrWhiteSpace(InputMessage))
            return;

        var userMessage = InputMessage.Trim();
        InputMessage = string.Empty;
        ErrorMessage = null;

        // Добавляем сообщение пользователя в UI
        var userMessageModel = new ChatMessageModel(
            Guid.NewGuid(),
            MessageNodeType.User,
            userMessage,
            DateTime.UtcNow);
        Messages.Add(userMessageModel);

        // Создаём placeholder для ответа ассистента
        var assistantMessageModel = new ChatMessageModel(
            Guid.NewGuid(),
            MessageNodeType.Assistant,
            string.Empty,
            DateTime.UtcNow)
        {
            IsStreaming = true
        };
        Messages.Add(assistantMessageModel);

        try
        {
            IsLoading = true;

            // Отправляем сообщение с потоковой передачей
            var response = await _chatService.SendMessageStreamingAsync(
                CurrentConversation.Id,
                userMessage,
                chunk =>
                {
                    // Обновляем UI в главном потоке
                    Dispatcher.UIThread.Post(() =>
                    {
                        assistantMessageModel.AppendContent(chunk);
                    });
                });

            // Обновляем ID сообщения после сохранения
            assistantMessageModel.Id = response.Id;
            assistantMessageModel.IsStreaming = false;

            _logger.LogDebug("Received response from AI: {ResponseLength} chars", response.Content.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message");
            
            // Удаляем placeholder ответа
            Messages.Remove(assistantMessageModel);
            
            ErrorMessage = $"Ошибка отправки сообщения: {ex.Message}";
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

    partial void OnInputMessageChanged(string value)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
    }

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
    /// Начинает редактирование сообщения
    /// </summary>
    [RelayCommand]
    private void StartEditMessage(ChatMessageModel message)
    {
        message.IsEditing = true;
        message.EditedContent = message.Content;
    }

    /// <summary>
    /// Сохраняет отредактированное сообщение и создаёт новую ветку
    /// </summary>
    [RelayCommand]
    private async Task SaveEditedMessageAsync(ChatMessageModel message)
    {
        if (CurrentConversation == null || !message.ParentId.HasValue)
            return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            // 1. Create a branch from the message's parent
            var branchName = $"Edit-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            var newBranch = await _conversationService.CreateBranchAsync(
                CurrentConversation.Id,
                message.ParentId.Value,
                branchName,
                cancellationToken: default);

            // 2. Switch to the new branch
            await _conversationService.SwitchBranchAsync(
                CurrentConversation.Id,
                newBranch.Id,
                cancellationToken: default);

            // 3. Send the edited message
            var editedText = message.EditedContent.Trim();

            // Create placeholder for assistant response
            var assistantMessageModel = new ChatMessageModel(
                Guid.NewGuid(),
                MessageNodeType.Assistant,
                string.Empty,
                DateTime.UtcNow)
            {
                IsStreaming = true
            };

            // Send message with streaming
            var response = await _chatService.SendMessageStreamingAsync(
                CurrentConversation.Id,
                editedText,
                chunk =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        assistantMessageModel.AppendContent(chunk);
                    });
                });

            assistantMessageModel.Id = response.Id;
            assistantMessageModel.IsStreaming = false;

            // 4. Reload messages
            message.IsEditing = false;
            await LoadMessagesAsync(default);

            _logger.LogInformation("Created new branch {BranchId} from edited message", newBranch.Id);
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
    private void CancelEditMessage(ChatMessageModel message)
    {
        message.IsEditing = false;
        message.EditedContent = string.Empty;
    }

    /// <summary>
    /// Переходит к предыдущему sibling сообщению
    /// </summary>
    [RelayCommand]
    private async Task NavigateToPreviousSiblingAsync(ChatMessageModel message)
    {
        if (CurrentConversation == null || !message.ParentId.HasValue)
            return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            // Get all siblings
            var siblings = await _messageNodeRepository.GetChildrenAsync(
                message.ParentId.Value,
                cancellationToken: default);

            var siblingList = siblings
                .Where(s => s.NodeType == message.NodeType)
                .OrderBy(s => s.CreatedAt)
                .ToList();

            if (siblingList.Count <= 1)
                return;

            var currentIndex = siblingList.FindIndex(s => s.Id == message.Id);
            if (currentIndex <= 0)
                return;

            var previousSibling = siblingList[currentIndex - 1];

            // Find or create a branch containing this sibling
            await SwitchToBranchContainingNodeAsync(previousSibling.Id);

            // Reload UI
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
        if (CurrentConversation == null || !message.ParentId.HasValue)
            return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            // Get all siblings
            var siblings = await _messageNodeRepository.GetChildrenAsync(
                message.ParentId.Value,
                cancellationToken: default);

            var siblingList = siblings
                .Where(s => s.NodeType == message.NodeType)
                .OrderBy(s => s.CreatedAt)
                .ToList();

            if (siblingList.Count <= 1)
                return;

            var currentIndex = siblingList.FindIndex(s => s.Id == message.Id);
            if (currentIndex < 0 || currentIndex >= siblingList.Count - 1)
                return;

            var nextSibling = siblingList[currentIndex + 1];

            // Find or create a branch containing this sibling
            await SwitchToBranchContainingNodeAsync(nextSibling.Id);

            // Reload UI
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
    /// Обновляет информацию о sibling узлах для сообщения
    /// </summary>
    private async Task UpdateSiblingInfoAsync(ChatMessageModel message, CancellationToken cancellationToken)
    {
        if (!message.ParentId.HasValue)
            return;

        var siblings = await _messageNodeRepository.GetChildrenAsync(
            message.ParentId.Value,
            cancellationToken);

        var siblingList = siblings
            .Where(s => s.NodeType == message.NodeType)
            .OrderBy(s => s.CreatedAt)
            .ToList();

        if (siblingList.Count > 1)
        {
            var currentIndex = siblingList.FindIndex(s => s.Id == message.Id);
            message.TotalSiblings = siblingList.Count;
            message.CurrentSiblingIndex = currentIndex + 1;
            message.HasPreviousSibling = currentIndex > 0;
            message.HasNextSibling = currentIndex < siblingList.Count - 1;
        }
    }

    /// <summary>
    /// Находит или создаёт ветку, содержащую указанный узел, и переключается на неё
    /// </summary>
    private async Task SwitchToBranchContainingNodeAsync(Guid nodeId)
    {
        if (CurrentConversation == null)
            return;

        // Get all branches for this conversation
        var branches = await _branchRepository.GetByConversationIdAsync(
            CurrentConversation.Id,
            cancellationToken: default);

        // Check each branch to see if it contains the node
        foreach (var branch in branches)
        {
            var path = await _conversationService.BuildContextAsync(branch.HeadNodeId, default);
            if (path.Any(n => n.Id == nodeId))
            {
                // Found a branch containing this node
                await _conversationService.SwitchBranchAsync(
                    CurrentConversation.Id,
                    branch.Id,
                    cancellationToken: default);
                return;
            }
        }

        // If no branch contains this node, create a new branch from it
        var newBranch = await _conversationService.CreateBranchAsync(
            CurrentConversation.Id,
            nodeId,
            $"Branch-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
            cancellationToken: default);

        await _conversationService.SwitchBranchAsync(
            CurrentConversation.Id,
            newBranch.Id,
            cancellationToken: default);

        _logger.LogInformation("Created new branch {BranchId} for node {NodeId}", newBranch.Id, nodeId);
    }
}
