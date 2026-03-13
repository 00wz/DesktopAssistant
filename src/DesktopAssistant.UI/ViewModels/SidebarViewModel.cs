using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.UI.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel боковой панели диалогов.
/// Управляет списком диалогов, их сессионными статусами и навигацией.
/// <para>
/// Связь с <see cref="MainWindowViewModel"/> — только через делегаты
/// <see cref="OnConversationSelected"/> и <see cref="OnNewChatRequested"/>,
/// без прямой зависимости в обе стороны.
/// </para>
/// <para>
/// Спроектирован для расширения: замена плоского <see cref="Conversations"/>
/// на иерархическую коллекцию потребует изменений только в этом классе.
/// </para>
/// </summary>
public partial class SidebarViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConversationSessionService _sessionService;
    private readonly ILogger<SidebarViewModel> _logger;

    /// <summary>Вызывается, когда пользователь выбирает диалог из списка.</summary>
    public Func<ConversationListItemViewModel, Task>? OnConversationSelected { get; set; }

    /// <summary>Вызывается при нажатии кнопки «Новый чат» в заголовке панели.</summary>
    public Func<Task>? OnNewChatRequested { get; set; }

    /// <summary>Вызывается при нажатии кнопки «Настройки» в нижней части панели.</summary>
    public Func<Task>? OnSettingsRequested { get; set; }

    /// <summary>
    /// Плоский список диалогов. В будущем будет заменён иерархической коллекцией
    /// узлов без изменений в <see cref="MainWindowViewModel"/>.
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

    // ── Загрузка данных ──────────────────────────────────────────────────────

    /// <summary>
    /// Загружает список диалогов из БД и синхронизирует состояние активных сессий.
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

    // ── Управление состоянием (вызывается из MainWindowViewModel) ────────────

    /// <summary>Подключает сессию к соответствующему элементу списка.</summary>
    public void AttachSession(Guid conversationId, IConversationSession session)
        => FindItem(conversationId)?.AttachSession(session);

    /// <summary>Отключает сессию от соответствующего элемента списка.</summary>
    public void DetachSession(Guid conversationId)
        => FindItem(conversationId)?.DetachSession();

    /// <summary>Обновляет флаг <see cref="ConversationListItemViewModel.IsSelected"/> для всех элементов.</summary>
    public void MarkSelected(Guid? conversationId)
    {
        foreach (var item in Conversations)
            item.IsSelected = item.Id == conversationId;
    }

    // ── Команды ──────────────────────────────────────────────────────────────

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

    // ── Вспомогательные ──────────────────────────────────────────────────────

    private ConversationListItemViewModel? FindItem(Guid id)
        => Conversations.FirstOrDefault(c => c.Id == id);
}
