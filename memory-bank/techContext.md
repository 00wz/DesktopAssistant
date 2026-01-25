# DesktopAssistant - Technical Context (v2)

## Обновления после ревью этапа 1

### Изменения в архитектуре

1. **Unified Kernel Factory** — единый подход к созданию Kernel для всех провайдеров
2. **Piper TTS** — офлайн синтез речи вместо Azure Speech
3. **MCP через Semantic Kernel** — использование встроенной интеграции SK + ModelContextProtocol
4. **Summary Node** — специальный тип узла для хранения суммаризированного контекста

## Среда разработки

- **.NET Version**: .NET 9.0
- **Language**: C# 13
- **IDE**: Visual Studio 2022 / VS Code / JetBrains Rider

## Зависимости проекта (обновлённые)

### Infrastructure Layer (DesktopAssistant.Infrastructure)

```xml
<!-- Semantic Kernel (единый коннектор) -->
<PackageReference Include="Microsoft.SemanticKernel" Version="1.32.0" />
<PackageReference Include="Microsoft.SemanticKernel.Connectors.OpenAI" Version="1.32.0" />

<!-- MCP Integration -->
<PackageReference Include="ModelContextProtocol" Version="0.1.0-preview" />

<!-- Database -->
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.0" />

<!-- Speech Recognition (offline) -->
<PackageReference Include="Vosk" Version="0.3.38" />

<!-- Text-to-Speech (offline) - Piper TTS -->
<!-- Piper использует ONNX Runtime -->
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.19.0" />

<!-- Audio -->
<PackageReference Include="NAudio" Version="2.2.1" />

<!-- Configuration & Logging -->
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="9.0.0" />
<PackageReference Include="Serilog" Version="4.1.0" />
<PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
<PackageReference Include="Serilog.Extensions.Logging" Version="8.0.0" />
```

## LLM провайдеры (Unified Approach)

Все провайдеры используют OpenAI-совместимый API через единый коннектор:

```csharp
public class KernelFactory : IKernelFactory
{
    private readonly LlmOptions _options;

    public Kernel Create()
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(_options.BaseUrl)
        };

        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(
            _options.Model,
            _options.ApiKey,
            orgId: null,
            serviceId: null,
            httpClient: httpClient
        );

        return builder.Build();
    }
}
```

### Поддерживаемые провайдеры (все через OpenAI-совместимый API)

| Провайдер | BaseUrl |
|-----------|---------|
| OpenAI | https://api.openai.com/v1 |
| Azure OpenAI | https://{resource}.openai.azure.com/openai/deployments/{deployment} |
| Anthropic (via proxy) | https://api.anthropic.com/v1 |
| Ollama | http://localhost:11434/v1 |
| LM Studio | http://localhost:1234/v1 |
| Together AI | https://api.together.xyz/v1 |

## Text-to-Speech (Piper TTS - Offline)

### Архитектура

Piper TTS — быстрый офлайн нейросетевой синтезатор речи на основе VITS:

```
Text Input → Phonemizer → ONNX Model → Audio Output (WAV/PCM)
```

### Модели Piper

| Язык | Модель | Размер | Качество |
|------|--------|--------|----------|
| English | en_US-amy-medium | ~60MB | Высокое |
| Russian | ru_RU-irina-medium | ~70MB | Высокое |
| German | de_DE-thorsten-high | ~80MB | Очень высокое |

### Интеграция с .NET

```csharp
public interface IPiperTtsService
{
    Task<byte[]> SynthesizeAsync(string text, string voiceModel);
    IAsyncEnumerable<byte[]> StreamSynthesizeAsync(string text, string voiceModel);
    Task<IEnumerable<PiperVoice>> GetAvailableVoicesAsync();
}
```

## MCP Integration с Semantic Kernel

Semantic Kernel поддерживает MCP через библиотеку `ModelContextProtocol`:

```csharp
// Подключение MCP сервера и создание плагина
var mcpClient = await McpClient.ConnectAsync(serverConfig);
var tools = await mcpClient.ListToolsAsync();

// Конвертация MCP tools в SK functions
var plugin = tools.ToKernelPlugin("mcp_server_name");
kernel.Plugins.Add(plugin);

// Теперь SK может автоматически вызывать MCP tools через function calling
```

### Конфигурация MCP серверов

```json
{
  "McpServers": [
    {
      "Name": "filesystem",
      "Type": "Stdio",
      "Command": "npx",
      "Args": ["-y", "@anthropic-ai/mcp-filesystem-server", "/allowed/path"]
    },
    {
      "Name": "web-search",
      "Type": "Sse",
      "Url": "https://mcp.example.com/search"
    }
  ]
}
```

## Граф истории диалога с Summary Node

### Типы узлов сообщений

```csharp
public enum MessageNodeType
{
    System = 0,      // Системный промпт
    User = 1,        // Сообщение пользователя
    Assistant = 2,   // Ответ ассистента
    Summary = 3      // Узел суммаризации
}
```

### Логика суммаризации

```
Диалог короткий (< threshold токенов):
[System] → [User] → [Assistant] → [User] → [Assistant] → ...

Диалог длинный (>= threshold токенов):
[System] → [User] → [Assistant] → ... → [SUMMARY NODE] → [User] → [Assistant] → ...
                                              ↑
                                    "Сводка предыдущего диалога:
                                     - Обсудили тему X
                                     - Пользователь попросил Y
                                     - Решено сделать Z"
```

### Сборка контекста для LLM

```csharp
public async Task<IEnumerable<ChatMessage>> BuildContextAsync(Guid headNodeId)
{
    var messages = new List<ChatMessage>();
    var currentNode = await GetNodeAsync(headNodeId);
    
    // Идём назад по ветке
    while (currentNode != null)
    {
        if (currentNode.Type == MessageNodeType.Summary)
        {
            // Нашли summary — используем его как точку отсчёта
            messages.Insert(0, new ChatMessage(MessageRole.System, currentNode.Content));
            break;
        }
        
        messages.Insert(0, ToChatMessage(currentNode));
        currentNode = await GetParentAsync(currentNode);
    }
    
    return messages;
}
```

## Конфигурация приложения

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=desktopassistant.db"
  },
  "LlmOptions": {
    "BaseUrl": "https://api.openai.com/v1",
    "ApiKey": "",
    "Model": "gpt-4o-mini"
  },
  "Speech": {
    "VoskModelPath": "models/vosk",
    "PiperModelsPath": "models/piper",
    "DefaultLanguage": "en-US",
    "DefaultVoice": "en_US-amy-medium"
  },
  "Summarization": {
    "TokenThreshold": 6000,
    "SummaryMaxTokens": 500
  },
  "McpServers": []
}
```

## Кроссплатформенные особенности

### Piper TTS

- Windows: ONNX Runtime CPU/DirectML
- macOS: ONNX Runtime CPU/CoreML
- Linux: ONNX Runtime CPU

### Vosk

- Работает нативно на всех платформах
- Требует скачивания языковой модели (~50-200MB)

### NAudio

- Windows: WaveOut, WASAPI
- macOS/Linux: требуется портирование на OpenAL или SDL2
