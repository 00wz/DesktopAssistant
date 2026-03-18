using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.UI.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel for the main window.
/// Orchestrates the lifecycle of conversations (ChatViewModel cache, sessions) and
/// manages the visibility of overlay panels (settings, create chat).
/// Conversation list logic is extracted into <see cref="SidebarViewModel"/>.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IConversationSessionService _conversationSessionService;

    /// <summary>ChatViewModel cache keyed by conversation Id — preserves UI state when switching.</summary>
    private readonly Dictionary<Guid, ChatViewModel> _chatViewModels = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveChat))]
    private ChatViewModel? _selectedChat;

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    /// <summary>New conversation creation panel (null when hidden).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewConversationPanelVisible))]
    private NewConversationPanelViewModel? _newConversationPanel;

    public bool IsNewConversationPanelVisible => NewConversationPanel != null;

    /// <summary>Settings panel (null when hidden).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsVisible))]
    private SettingsViewModel? _settingsView;

    public bool IsSettingsVisible => SettingsView != null;

    /// <summary>True if a conversation is displayed in the main area.</summary>
    public bool HasActiveChat => SelectedChat != null;

    /// <summary>ViewModel for the conversation sidebar.</summary>
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
        Sidebar.OnSettingsRequested = OpenSettingsAsync;

        _conversationSessionService.SessionReleased += OnSessionReleased;
    }

    public async Task InitializeAsync()
    {
        await Sidebar.LoadConversationsAsync();
        _logger.LogInformation("Main window initialized");
    }

    // ── Conversation navigation ───────────────────────────────────────────────

    /// <summary>
    /// Hides all overlay panels. Called before any navigation to a conversation
    /// so that each navigation command does not need to enumerate every panel.
    /// When adding new overlays, only this method needs to be updated.
    /// </summary>
    private void CloseOverlays()
    {
        NewConversationPanel = null;
        SettingsView = null;
    }

    /// <summary>
    /// Opens a conversation: creates or restores a session and ChatViewModel, and makes it active.
    /// Called via <see cref="SidebarViewModel.OnConversationSelected"/>.
    /// </summary>
    private async Task OpenSelectedConversationAsync(ConversationListItemViewModel item)
    {
        CloseOverlays();

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

    // ── Overlay panels ────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task OpenSettingsAsync()
    {
        try
        {
            var vm = _serviceProvider.GetRequiredService<SettingsViewModel>();
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
            CloseOverlays();
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
                parameters.SystemPrompt,
                parameters.Mode);

            var session = await _conversationSessionService.GetOrCreate(conversation.Id);

            var chatViewModel = _serviceProvider.GetRequiredService<ChatViewModel>();
            await chatViewModel.InitializeAsync(session);
            _chatViewModels[conversation.Id] = chatViewModel;

            // Reload the list — the new conversation will appear with the session already attached.
            // MarkSelected is called automatically via OnSelectedChatChanged after
            // SelectedChat is set.
            await Sidebar.LoadConversationsAsync();

            SelectedChat = chatViewModel;

            await session.SendMessageAsync(parameters.FirstMessage);
            _logger.LogInformation("Created new chat: {Title}", parameters.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating new chat");
        }
    }
}
