using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.UI.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel главного окна.
/// Оркестрирует жизненный цикл диалогов (кэш ChatViewModel, сессии) и
/// управляет видимостью оверлейных панелей (настройки, создание чата).
/// Логика списка диалогов вынесена в <see cref="SidebarViewModel"/>.
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

    /// <summary>True если в основной области отображается диалог.</summary>
    public bool HasActiveChat => SelectedChat != null;

    /// <summary>ViewModel боковой панели диалогов.</summary>
    public SidebarViewModel Sidebar { get; }

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

        Sidebar = _serviceProvider.GetRequiredService<SidebarViewModel>();
        Sidebar.OnConversationSelected = OpenSelectedConversationAsync;
        Sidebar.OnNewChatRequested = CreateNewChatAsync;

        _conversationSessionService.SessionReleased += OnSessionReleased;
    }

    public async Task InitializeAsync()
    {
        await Sidebar.LoadConversationsAsync();
        _logger.LogInformation("Main window initialized");
    }

    // ── Навигация диалогов ────────────────────────────────────────────────────

    /// <summary>
    /// Открывает диалог: создаёт/восстанавливает сессию и ChatViewModel, делает его активным.
    /// Вызывается через <see cref="SidebarViewModel.OnConversationSelected"/>.
    /// </summary>
    private async Task OpenSelectedConversationAsync(ConversationListItemViewModel item)
    {
        NewConversationPanel = null;

        try
        {
            var session = await _conversationSessionService.GetOrCreate(item.Id);

            if (!item.IsActive)
                item.AttachSession(session);

            if (!_chatViewModels.TryGetValue(item.Id, out var chatViewModel))
            {
                chatViewModel = _serviceProvider.GetRequiredService<ChatViewModel>();
                await chatViewModel.InitializeAsync(session);
                _chatViewModels[item.Id] = chatViewModel;
            }

            SelectedChat = chatViewModel;
            _logger.LogInformation("Opened conversation {Id}", item.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening conversation {Id}", item.Id);
        }
    }

    partial void OnSelectedChatChanged(ChatViewModel? value)
        => Sidebar.MarkSelected(value?.ConversationId);

    private void OnSessionReleased(object? sender, Guid conversationId)
    {
        Sidebar.DetachSession(conversationId);
        _chatViewModels.Remove(conversationId);

        if (SelectedChat?.ConversationId == conversationId)
            SelectedChat = null;
    }

    // ── Оверлейные панели ─────────────────────────────────────────────────────

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

            // Перезагружаем список — новый диалог появится с уже присоединённой сессией.
            // MarkSelected вызывается автоматически через OnSelectedChatChanged после
            // установки SelectedChat.
            await Sidebar.LoadConversationsAsync();

            SelectedChat = chatViewModel;
            _logger.LogInformation("Created new chat: {Title}", parameters.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating new chat");
        }
    }
}
