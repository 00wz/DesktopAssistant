# DesktopAssistant - System Patterns

## Архитектурный стиль

Приложение построено на основе **Clean Architecture** с разделением на слои:

```
┌─────────────────────────────────────────────────────────────┐
│                    Presentation Layer                        │
│              (Avalonia UI + MVVM ViewModels)                │
├─────────────────────────────────────────────────────────────┤
│                   Application Layer                          │
│          (Use Cases, Services, Event Handlers)              │
├─────────────────────────────────────────────────────────────┤
│                     Domain Layer                             │
│        (Entities, Value Objects, Domain Events)             │
├─────────────────────────────────────────────────────────────┤
│                 Infrastructure Layer                         │
│    (Database, External APIs, Speech Services, MCP)          │
└─────────────────────────────────────────────────────────────┘
```

## Структура решения

```
DesktopAssistant/
├── src/
│   ├── DesktopAssistant.Domain/           # Доменный слой
│   │   ├── Entities/
│   │   │   ├── Conversation.cs
│   │   │   ├── Message.cs
│   │   │   ├── MessageNode.cs             # Узел графа сообщений
│   │   │   ├── ConversationBranch.cs      # Ветка истории
│   │   │   └── Assistant.cs
│   │   ├── ValueObjects/
│   │   │   ├── AssistantSettings.cs
│   │   │   └── LlmProviderConfig.cs
│   │   ├── Events/
│   │   │   ├── MessageReceivedEvent.cs
│   │   │   └── BranchCreatedEvent.cs
│   │   └── Interfaces/
│   │       ├── IConversationRepository.cs
│   │       └── IMessageRepository.cs
│   │
│   ├── DesktopAssistant.Application/      # Слой приложения
│   │   ├── Services/
│   │   │   ├── ConversationService.cs
│   │   │   ├── ChatService.cs
│   │   │   ├── SummarizationService.cs
│   │   │   └── SettingsService.cs
│   │   ├── UseCases/
│   │   │   ├── SendMessageUseCase.cs
│   │   │   ├── CreateBranchUseCase.cs
│   │   │   └── SwitchBranchUseCase.cs
│   │   └── Interfaces/
│   │       ├── ILlmService.cs
│   │       ├── ISpeechRecognitionService.cs
│   │       ├── ITextToSpeechService.cs
│   │       └── IMcpClientService.cs
│   │
│   ├── DesktopAssistant.Infrastructure/   # Инфраструктурный слой
│   │   ├── Persistence/
│   │   │   ├── AppDbContext.cs
│   │   │   ├── Repositories/
│   │   │   └── Migrations/
│   │   ├── AI/
│   │   │   ├── SemanticKernelService.cs
│   │   │   └── Providers/
│   │   │       ├── OpenAIProvider.cs
│   │   │       ├── AzureOpenAIProvider.cs
│   │   │       └── AnthropicProvider.cs
│   │   ├── Speech/
│   │   │   ├── VoskSpeechRecognition.cs
│   │   │   ├── WakeWordDetector.cs
│   │   │   └── TextToSpeech/
│   │   │       ├── AzureTtsService.cs
│   │   │       └── OpenAITtsService.cs
│   │   ├── MCP/
│   │   │   ├── McpClientManager.cs
│   │   │   └── McpToolExecutor.cs
│   │   └── Audio/
│   │       ├── AudioCapture.cs
│   │       └── AudioPlayback.cs
│   │
│   └── DesktopAssistant.UI/               # Презентационный слой
│       ├── App.axaml
│       ├── App.axaml.cs
│       ├── ViewModels/
│       │   ├── MainWindowViewModel.cs
│       │   ├── ConversationTabViewModel.cs
│       │   ├── ChatViewModel.cs
│       │   ├── SettingsViewModel.cs
│       │   └── FirstSetupViewModel.cs
│       ├── Views/
│       │   ├── MainWindow.axaml
│       │   ├── ConversationTabView.axaml
│       │   ├── ChatView.axaml
│       │   ├── SettingsView.axaml
│       │   └── FirstSetupView.axaml
│       ├── Controls/
│       │   ├── MessageBubble.axaml
│       │   ├── BranchNavigator.axaml
│       │   └── VoiceIndicator.axaml
│       ├── Converters/
│       ├── Services/
│       │   └── DialogService.cs
│       └── Assets/
│
├── tests/
│   ├── DesktopAssistant.Domain.Tests/
│   ├── DesktopAssistant.Application.Tests/
│   └── DesktopAssistant.Infrastructure.Tests/
│
├── docs/
│   ├── architecture.md
│   └── api.md
│
└── memory-bank/
```

## Ключевые паттерны

### 1. MVVM (Model-View-ViewModel)

Используется CommunityToolkit.Mvvm для реализации:

```csharp
public partial class ChatViewModel : ObservableObject
{
    [ObservableProperty]
    private string _userInput = string.Empty;
    
    [ObservableProperty]
    private ObservableCollection<MessageViewModel> _messages = new();
    
    [RelayCommand]
    private async Task SendMessage()
    {
        // ...
    }
}
```

### 2. Repository Pattern

Абстракция доступа к данным:

```csharp
public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(Guid id);
    Task<IEnumerable<Conversation>> GetAllAsync();
    Task AddAsync(Conversation conversation);
    Task UpdateAsync(Conversation conversation);
    Task SoftDeleteAsync(Guid id);
}
```

### 3. Event-Driven Architecture

Для коммуникации между компонентами:

```csharp
public class MessageReceivedEvent
{
    public Guid ConversationId { get; init; }
    public Message Message { get; init; }
}

// Публикация события
_eventAggregator.Publish(new MessageReceivedEvent { ... });

// Подписка
_eventAggregator.Subscribe<MessageReceivedEvent>(OnMessageReceived);
```

### 4. Strategy Pattern для LLM провайдеров

```csharp
public interface ILlmProvider
{
    string Name { get; }
    Task<IAsyncEnumerable<string>> StreamChatAsync(
        IEnumerable<Message> messages, 
        CancellationToken ct);
}
```

### 5. Graph-based History (Граф истории диалога)

```
       [System Prompt]
             │
         [User: Hi]
             │
      [Assistant: Hello!]
             │
    ┌────────┴────────┐
    │                 │
[User: Weather?]  [User: Code?]  ← Ветвление
    │                 │
[Assist: Sunny]   [Assist: Sure]
```

## Потоки данных

### Отправка сообщения

```
User Input → ChatViewModel → SendMessageUseCase → ConversationService
                                                        │
                                    ┌───────────────────┤
                                    ▼                   ▼
                            LlmService          Repository.Save()
                                    │
                                    ▼
                            Stream Response → UI Update → TTS (optional)
```

### Голосовой ввод

```
Microphone → AudioCapture → VoskRecognition → WakeWordDetector
                                    │                 │
                            (transcription)     (wake detected)
                                    │                 │
                                    ▼                 ▼
                            ChatViewModel ← Activate App
```

## Конфигурация и DI

```csharp
// Program.cs
services.AddSingleton<ILlmService, SemanticKernelService>();
services.AddSingleton<ISpeechRecognitionService, VoskSpeechRecognition>();
services.AddSingleton<ITextToSpeechService, AzureTtsService>();
services.AddTransient<IConversationRepository, ConversationRepository>();
services.AddDbContext<AppDbContext>(options => 
    options.UseSqlite(connectionString));
```

## Обработка ошибок

- Глобальный обработчик исключений для UI
- Try-catch в критических операциях
- Логирование через Serilog
- Graceful degradation для речевых сервисов
