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
    public async Task InitializeAsync()
    {
        try
        {
            // Создаём начальную вкладку чата
            await CreateNewChatAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing main window");
        }
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

        if (Chats.Count == 0)
        {
            // Если закрыли последнюю вкладку - создаём новую
            _ = CreateNewChatAsync();
        }
        else if (index >= Chats.Count)
        {
            SelectedTabIndex = Chats.Count - 1;
        }
        else
        {
            SelectedTabIndex = index;
        }

        _logger.LogInformation("Closed chat tab");
    }
}
