using System.Collections.ObjectModel;
using Avalonia.Threading;
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
/// Supports hierarchical conversations: <see cref="Conversations"/> contains only
/// root-level items; children are nested via <see cref="ConversationListItemViewModel.Children"/>.
/// The flat <see cref="_index"/> dictionary provides O(1) lookup by id.
/// </para>
/// </summary>
public partial class SidebarViewModel : ObservableObject, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConversationSessionService _sessionService;
    private readonly ILogger<SidebarViewModel> _logger;

    /// <summary>O(1) lookup map; contains every item regardless of depth.</summary>
    private readonly Dictionary<Guid, ConversationListItemViewModel> _index = new();

    /// <summary>Tracks the currently selected conversation so selection survives list reloads.</summary>
    private Guid? _selectedId;

    /// <summary>Called when the user selects a conversation from the list.</summary>
    public Func<ConversationListItemViewModel, Task>? OnConversationSelected { get; set; }

    /// <summary>Called when the "New Chat" button in the panel header is pressed.</summary>
    public Func<Task>? OnNewChatRequested { get; set; }

    /// <summary>Called when the "Settings" button at the bottom of the panel is pressed.</summary>
    public Func<Task>? OnSettingsRequested { get; set; }

    /// <summary>
    /// Root-level conversations. Children are accessed via
    /// <see cref="ConversationListItemViewModel.Children"/> for each item.
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

        _sessionService.SessionCreated += OnSessionCreated;
        _sessionService.SessionReleased += OnSessionReleased;
    }

    // ── Data loading ─────────────────────────────────────────────────────────

    /// <summary>
    /// Loads conversations from the database, builds a tree by ParentId,
    /// and synchronizes the state of active sessions.
    /// Re-applies the current selection after the list is rebuilt.
    /// </summary>
    public async Task LoadConversationsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            var conversations = await chatService.GetConversationsAsync();

            // Dispose and clear existing items
            foreach (var item in _index.Values)
                item.Dispose();
            Conversations.Clear();
            _index.Clear();

            var activeIds = _sessionService.ActiveSessionIds;
            var ordered = conversations.OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt).ToList();

            // ── Pass 1: create all ViewModels and populate index ─────────────
            var allItems = new Dictionary<Guid, ConversationListItemViewModel>();
            foreach (var conv in ordered)
            {
                var model = new ConversationListItem
                {
                    Id       = conv.Id,
                    Title    = conv.Title,
                    UpdatedAt = conv.UpdatedAt ?? conv.CreatedAt,
                    // ParentId will be mapped here once ConversationDto exposes it.
                    // For now all conversations are root-level.
                    ParentId = null
                };
                allItems[conv.Id] = new ConversationListItemViewModel(model);
            }

            // ── Pass 2: build tree ────────────────────────────────────────────
            foreach (var item in allItems.Values)
            {
                if (item.ParentId.HasValue && allItems.TryGetValue(item.ParentId.Value, out var parent))
                    parent.Children.Add(item);
                else
                    Conversations.Add(item);

                _index[item.Id] = item;
            }

            // ── Pass 3: attach active sessions ───────────────────────────────
            foreach (var id in activeIds)
            {
                if (_index.TryGetValue(id, out var item))
                {
                    var session = await _sessionService.GetOrCreate(id);
                    item.AttachSession(session);
                }
            }

            // ── Pass 4: restore selection ─────────────────────────────────────
            if (_selectedId.HasValue && _index.TryGetValue(_selectedId.Value, out var selected))
                selected.IsSelected = true;

            _logger.LogDebug("Loaded {Count} conversations ({Roots} roots)", _index.Count, Conversations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading conversations");
        }
    }

    // ── State management (called from MainWindowViewModel) ───────────────────

    /// <summary>Updates the <see cref="ConversationListItemViewModel.IsSelected"/> flag for all items.</summary>
    public void MarkSelected(Guid? conversationId)
    {
        _selectedId = conversationId;
        foreach (var item in _index.Values)
            item.IsSelected = item.Id == conversationId;
    }

    public void Dispose()
    {
        _sessionService.SessionCreated -= OnSessionCreated;
        _sessionService.SessionReleased -= OnSessionReleased;
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
        => _index.GetValueOrDefault(id);

    private async void OnSessionCreated(object? sender, Guid conversationId)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(LoadConversationsAsync);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reloading sidebar after session created for {Id}", conversationId);
        }
    }

    private void OnSessionReleased(object? sender, Guid conversationId)
        => FindItem(conversationId)?.DetachSession();
}
