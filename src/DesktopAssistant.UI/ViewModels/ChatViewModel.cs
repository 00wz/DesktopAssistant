using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.UI.Models;
using Microsoft.Extensions.Logging;

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

    public ObservableCollection<ChatMessageModel> Messages { get; } = new();

    public ChatViewModel(IChatService chatService, ILogger<ChatViewModel> logger)
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
        
        foreach (var message in messages)
        {
            // Не показываем системные сообщения в UI
            if (message.NodeType != MessageNodeType.System)
            {
                Messages.Add(new ChatMessageModel(
                    message.Id,
                    message.NodeType,
                    message.Content,
                    message.CreatedAt));
            }
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
}
