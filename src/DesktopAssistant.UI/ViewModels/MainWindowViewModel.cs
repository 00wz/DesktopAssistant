using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.UI.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel главного окна: управляет боковой панелью диалогов и единственным активным ChatView.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IConversationSessionService _conversationSessionService;

    /// <summary>Кэш ChatViewModel по Id диалога — сохраняет состояние UI при переключении.</summary>
    private readonly Dictionary<Guid, ChatViewModel> _chatViewModels = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveChat))]
    private ChatViewModel? _selectedChat;

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    /// <summary>Панель создания нового диалога (null если скрыта).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewConversationPanelVisible))]
    private NewConversationPanelViewModel? _newConversationPanel;

    public bool IsNewConversationPanelVisible => NewConversationPanel != null;

    /// <summary>Панель настроек (null если скрыта).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsVisible))]
    private SettingsViewModel? _settingsView;

    public bool IsSettingsVisible => SettingsView != null;

    /// <summary>True если выбран активный диалог для отображения.</summary>
    public bool HasActiveChat => SelectedChat != null;

    public ObservableCollection<ConversationListItemViewModel> SavedConversations { get; } = new();

    public MainWindowViewModel(
        IServiceProvider serviceProvider,
        IServiceScopeFactory scopeFactory,
        ILogger<MainWindowViewModel> logger,
        IConversationSessionService conversationSessionService)
    {
        _serviceProvider = serviceProvider;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _conversationSessionService = conversationSessionService;

        _conversationSessionService.SessionReleased += OnSessionReleased;
    }

    public async Task InitializeAsync()
    {
        await LoadSavedConversationsAsync();
        _logger.LogInformation("Main window initialized");
    }

    /// <summary>
    /// Загружает список сохранённых диалогов. Для диалогов с уже существующими сессиями
    /// вызывает <see cref="ConversationListItemViewModel.AttachSession"/>.
    /// </summary>
    public async Task LoadSavedConversationsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            var conversations = await chatService.GetConversationsAsync();

            // Сохраняем id выбранного диалога для восстановления после перезагрузки
            var selectedId = SelectedChat?.ConversationId;

            // Очищаем старые VM (detach sessions, но не закрываем сессии в сервисе)
            foreach (var old in SavedConversations)
                old.Dispose();
            SavedConversations.Clear();

            var activeIds = _conversationSessionService.ActiveSessionIds;

            foreach (var conv in conversations.OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt))
            {
                var model = new ConversationListItem
                {
                    Id = conv.Id,
                    Title = conv.Title,
                    UpdatedAt = conv.UpdatedAt ?? conv.CreatedAt
                };

                var vm = new ConversationListItemViewModel(model);
                vm.IsSelected = conv.Id == selectedId;

                if (activeIds.Contains(conv.Id))
                {
                    var session = await _conversationSessionService.GetOrCreate(conv.Id);
                    vm.AttachSession(session);
                }

                SavedConversations.Add(vm);
            }

            _logger.LogDebug("Loaded {Count} saved conversations", SavedConversations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading saved conversations");
        }
    }

    /// <summary>
    /// Открывает диалог: создаёт или восстанавливает сессию и ChatViewModel, делает его активным.
    /// </summary>
    [RelayCommand]
    public async Task OpenConversationAsync(ConversationListItemViewModel? item)
    {
        if (item == null) return;

        NewConversationPanel = null;

        try
        {
            // Создаём или получаем сессию
            var session = await _conversationSessionService.GetOrCreate(item.Id);

            // Подключаем сессию к элементу списка (если ещё не подключена)
            if (!item.IsActive)
                item.AttachSession(session);

            // Создаём ChatViewModel при первом открытии, затем используем кэш
            if (!_chatViewModels.TryGetValue(item.Id, out var chatViewModel))
            {
                chatViewModel = _serviceProvider.GetRequiredService<ChatViewModel>();
                await chatViewModel.InitializeAsync(session);
                _chatViewModels[item.Id] = chatViewModel;
            }

            SelectedChat = chatViewModel;
            _logger.LogInformation("Opened conversation {ConversationId}: {Title}", item.Id, item.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening conversation {ConversationId}", item.Id);
        }
    }

    [RelayCommand]
    public async Task OpenSettingsAsync()
    {
        try
        {
            var vm = _serviceProvider.GetRequiredService<SettingsViewModel>();
            vm.OnClose = () => SettingsView = null;
            await vm.ProfilesSettings.LoadProfilesAsync();
            SettingsView = vm;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening settings");
        }
    }

    [RelayCommand]
    public void ToggleSidebar()
    {
        IsSidebarVisible = !IsSidebarVisible;
    }

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

            using var scope = _scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            var conversation = await chatService.CreateConversationAsync(
                parameters.Title,
                parameters.AssistantProfileId,
                parameters.SystemPrompt);

            var session = await _conversationSessionService.GetOrCreate(conversation.Id);

            var chatViewModel = _serviceProvider.GetRequiredService<ChatViewModel>();
            await chatViewModel.InitializeAsync(session);
            _chatViewModels[conversation.Id] = chatViewModel;

            // Обновляем список — новый диалог появится с уже присоединённой сессией
            await LoadSavedConversationsAsync();

            SelectedChat = chatViewModel;
            _logger.LogInformation("Created new chat: {Title}", parameters.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating new chat");
        }
    }

    // ── Обработчики событий сессионного сервиса ──────────────────────────────

    partial void OnSelectedChatChanged(ChatViewModel? value)
    {
        var activeId = value?.ConversationId;
        foreach (var item in SavedConversations)
            item.IsSelected = item.Id == activeId;
    }

    private void OnSessionReleased(object? sender, Guid conversationId)
    {
        var item = SavedConversations.FirstOrDefault(c => c.Id == conversationId);
        item?.DetachSession();

        _chatViewModels.Remove(conversationId);

        if (SelectedChat?.ConversationId == conversationId)
            SelectedChat = null;
    }
}
