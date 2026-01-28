using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel для главного окна с вкладками чатов
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MainWindowViewModel> _logger;

    [ObservableProperty]
    private ChatViewModel? _selectedChat;

    [ObservableProperty]
    private int _selectedTabIndex;

    public ObservableCollection<ChatViewModel> Chats { get; } = new();

    public MainWindowViewModel(IServiceProvider serviceProvider, ILogger<MainWindowViewModel> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Инициализация главного окна
    /// </summary>
    public Task InitializeAsync()
    {
        // При запуске показываем приветственный экран без автоматического создания чата
        _logger.LogInformation("Main window initialized");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Создаёт новую вкладку чата
    /// </summary>
    [RelayCommand]
    public async Task CreateNewChatAsync()
    {
        try
        {
            var chatViewModel = _serviceProvider.GetRequiredService<ChatViewModel>();
            chatViewModel.ConversationTitle = $"Чат {Chats.Count + 1}";
            
            await chatViewModel.InitializeAsync();
            
            Chats.Add(chatViewModel);
            SelectedChat = chatViewModel;
            SelectedTabIndex = Chats.Count - 1;
            
            _logger.LogInformation("Created new chat tab");
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

        // При закрытии последней вкладки показываем приветственный экран
        if (Chats.Count > 0)
        {
            if (index >= Chats.Count)
            {
                SelectedTabIndex = Chats.Count - 1;
            }
            else
            {
                SelectedTabIndex = index;
            }
        }
        else
        {
            SelectedChat = null;
        }

        _logger.LogInformation("Closed chat tab");
    }
}
