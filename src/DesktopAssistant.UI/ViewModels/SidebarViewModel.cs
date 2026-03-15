using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.UI.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel for the conversation sidebar panel.
/// Manages the conversation list, their session statuses, and navigation.
/// <para>
/// Communication with <see cref="MainWindowViewModel"/> is done only through delegates
/// <see cref="OnConversationSelected"/> and <see cref="OnNewChatRequested"/>,
/// without a direct two-way dependency.
/// </para>
/// <para>
/// Designed for extensibility: replacing the flat <see cref="Conversations"/>
/// with a hierarchical collection requires changes only in this class.
/// </para>
/// </summary>
public partial class SidebarViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConversationSessionService _sessionService;
    private readonly ILogger<SidebarViewModel> _logger;

    /// <summary>Called when the user selects a conversation from the list.</summary>
    public Func<ConversationListItemViewModel, Task>? OnConversationSelected { get; set; }

    /// <summary>Called when the "New Chat" button in the panel header is pressed.</summary>
    public Func<Task>? OnNewChatRequested { get; set; }

    /// <summary>Called when the "Settings" button at the bottom of the panel is pressed.</summary>
    public Func<Task>? OnSettingsRequested { get; set; }

    /// <summary>
    /// Flat list of conversations. Will be replaced in future by a hierarchical collection
    /// of nodes without changes to <see cref="MainWindowViewModel"/>.
    /// </summary>
    public ObservableCollection<ConversationListItemViewModel> Conversations { get; } = new();

    public SidebarViewModel(
        IServiceScopeFactory scopeFactory,
        IConversationSessionService sessionService,
        ILogger<SidebarViewModel> logger)
    {
        _scopeFactory = scopeFactory;
        _sessionService = sessionService;
        _logger = logger;
    }

    // ── Data loading ─────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the conversation list from the database and synchronizes the state of active sessions.
    /// </summary>
    public async Task LoadConversationsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            var conversations = await chatService.GetConversationsAsync();

            foreach (var old in Conversations)
                old.Dispose();
            Conversations.Clear();

            var activeIds = _sessionService.ActiveSessionIds;

            foreach (var conv in conversations.OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt))
            {
                var model = new ConversationListItem
                {
                    Id = conv.Id,
                    Title = conv.Title,
                    UpdatedAt = conv.UpdatedAt ?? conv.CreatedAt
                };

                var item = new ConversationListItemViewModel(model);

                if (activeIds.Contains(conv.Id))
                {
                    var session = await _sessionService.GetOrCreate(conv.Id);
                    item.AttachSession(session);
                }

                Conversations.Add(item);
            }

            _logger.LogDebug("Loaded {Count} conversations", Conversations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading conversations");
        }
    }

    // ── State management (called from MainWindowViewModel) ───────────────────

    /// <summary>Attaches a session to the corresponding list item.</summary>
    public void AttachSession(Guid conversationId, IConversationSession session)
        => FindItem(conversationId)?.AttachSession(session);

    /// <summary>Detaches the session from the corresponding list item.</summary>
    public void DetachSession(Guid conversationId)
        => FindItem(conversationId)?.DetachSession();

    /// <summary>Updates the <see cref="ConversationListItemViewModel.IsSelected"/> flag for all items.</summary>
    public void MarkSelected(Guid? conversationId)
    {
        foreach (var item in Conversations)
            item.IsSelected = item.Id == conversationId;
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenConversationAsync(ConversationListItemViewModel? item)
    {
        if (item == null || OnConversationSelected == null) return;
        await OnConversationSelected(item);
    }

    [RelayCommand]
    private Task CreateNewChatAsync()
        => OnNewChatRequested?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    private Task OpenSettingsAsync()
        => OnSettingsRequested?.Invoke() ?? Task.CompletedTask;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private ConversationListItemViewModel? FindItem(Guid id)
        => Conversations.FirstOrDefault(c => c.Id == id);
}
