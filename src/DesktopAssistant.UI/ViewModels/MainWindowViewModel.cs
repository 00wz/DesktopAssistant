using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.UI.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel для главного окна с вкладками чатов
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MainWindowViewModel> _logger;

    [ObservableProperty]
    private ChatViewModel? _selectedChat;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    [ObservableProperty]
    private ConversationListItem? _selectedConversation;

    /// <summary>Панель создания нового диалога (null если скрыта).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewConversationPanelVisible))]
    private NewConversationPanelViewModel? _newConversationPanel;

    public bool IsNewConversationPanelVisible => NewConversationPanel != null;

    public ObservableCollection<ChatViewModel> Chats { get; } = new();
    public ObservableCollection<ConversationListItem> SavedConversations { get; } = new();

    public MainWindowViewModel(
        IServiceProvider serviceProvider,
        IServiceScopeFactory scopeFactory,
        ILogger<MainWindowViewModel> logger)
    {
        _serviceProvider = serviceProvider;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Инициализация главного окна
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadSavedConversationsAsync();
        _logger.LogInformation("Main window initialized");
    }

    /// <summary>
    /// Загружает список сохранённых диалогов
    /// </summary>
    public async Task LoadSavedConversationsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            var conversations = await chatService.GetConversationsAsync();

            SavedConversations.Clear();
            foreach (var conv in conversations.OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt))
            {
                SavedConversations.Add(new ConversationListItem
                {
                    Id = conv.Id,
                    Title = conv.Title,
                    UpdatedAt = conv.UpdatedAt ?? conv.CreatedAt
                });
            }

            _logger.LogDebug("Loaded {Count} saved conversations", SavedConversations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading saved conversations");
        }
    }

    /// <summary>
    /// Открывает сохранённый диалог
    /// </summary>
    [RelayCommand]
    public async Task OpenConversationAsync(ConversationListItem? item)
    {
        if (item == null) return;

        // Скрываем панель создания если открыта
        NewConversationPanel = null;

        try
        {
            var existingChat = Chats.FirstOrDefault(c => c.CurrentConversation?.Id == item.Id);
            if (existingChat != null)
            {
                SelectedChat = existingChat;
                SelectedTabIndex = Chats.IndexOf(existingChat);
                return;
            }

            var chatViewModel = _serviceProvider.GetRequiredService<ChatViewModel>();
            await chatViewModel.InitializeAsync(item.Id);

            Chats.Add(chatViewModel);
            SelectedChat = chatViewModel;
            SelectedTabIndex = Chats.Count - 1;

            _logger.LogInformation("Opened conversation {ConversationId}: {Title}", item.Id, item.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening conversation {ConversationId}", item.Id);
        }
    }

    /// <summary>
    /// Переключает видимость боковой панели
    /// </summary>
    [RelayCommand]
    public void ToggleSidebar()
    {
        IsSidebarVisible = !IsSidebarVisible;
    }

    /// <summary>
    /// Открывает inline-панель создания нового диалога
    /// </summary>
    [RelayCommand]
    public async Task CreateNewChatAsync()
    {
        try
        {
            var panel = _serviceProvider.GetRequiredService<NewConversationPanelViewModel>();

            panel.OnConfirm = async (parameters) =>
            {
                await OpenNewChatWithParamsAsync(parameters);
            };

            panel.OnCancel = () =>
            {
                NewConversationPanel = null;
            };

            await panel.LoadProfilesAsync();
            NewConversationPanel = panel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening new chat panel");
        }
    }

    private async Task OpenNewChatWithParamsAsync(NewConversationParams parameters)
    {
        try
        {
            NewConversationPanel = null;

            var chatViewModel = _serviceProvider.GetRequiredService<ChatViewModel>();
            chatViewModel.ConversationTitle = parameters.Title;

            await chatViewModel.InitializeAsync(
                assistantProfileId: parameters.AssistantProfileId,
                systemPrompt: parameters.SystemPrompt);

            Chats.Add(chatViewModel);
            SelectedChat = chatViewModel;
            SelectedTabIndex = Chats.Count - 1;

            await LoadSavedConversationsAsync();

            _logger.LogInformation("Created new chat: {Title}", parameters.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating new chat");
        }
    }

    /// <summary>
    /// Закрывает вкладку чата
    /// </summary>
    [RelayCommand]
    public void CloseChat(ChatViewModel? chat)
    {
        if (chat == null) return;

        var index = Chats.IndexOf(chat);
        Chats.Remove(chat);

        if (Chats.Count > 0)
        {
            if (index >= Chats.Count)
                SelectedTabIndex = Chats.Count - 1;
            else
                SelectedTabIndex = index;
        }
        else
        {
            SelectedChat = null;
        }

        _logger.LogInformation("Closed chat tab");
    }
}
