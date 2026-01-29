# Стратегия самостоятельной установки MCP-серверов AI-ассистентом

## Обзор

Цель: AI-ассистент должен уметь самостоятельно находить, устанавливать и настраивать MCP-серверы для выполнения задач пользователя. Стратегия основана на подходе Cline с адаптацией под .NET/Semantic Kernel.

---

## Ключевые принципы

### 1. Задача как механизм установки

Вместо специализированного механизма установки используется обычная задача. AI выполняет её стандартными tools:
- `execute_command` - выполнение команд (git clone, npm install, npm run build)
- `write_to_file` - создание/редактирование файлов
- `read_file` - чтение файлов

Для получения информации от пользователя (например, API ключей) агент просто задаёт вопрос в чате и ожидает ответа в следующем сообщении.

### 2. Минимальный каталог MCP серверов

Локальный JSON-каталог с **минимальными** метаданными:
- GitHub URL (основной источник информации)
- Краткое описание возможностей
- Теги/категории для поиска

**Все детали установки** (команды, зависимости, требуемые API ключи) агент получает из README репозитория через tool `fetch_mcp_server_readme`.

### 3. README как источник истины

Tool `fetch_mcp_server_readme` загружает README.md из репозитория сервера. Агент анализирует README и самостоятельно определяет:
- Нужно ли клонировать репозиторий или достаточно npx
- Команды для установки и сборки
- Необходимые зависимости
- Требуемые API ключи и переменные окружения
- Формат добавления в конфигурацию

### 4. Native Tool Calls через Semantic Kernel

Каждый tool подключённого MCP сервера регистрируется как отдельная Kernel Function с автоматическим function calling. 

**Semantic Kernel автоматически передаёт descriptions tools и параметров в API** - дублирование в системном промпте не требуется.

---

## Архитектура

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          DesktopAssistant                               │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                      ChatService                                  │   │
│  │  ┌───────────────────┐  ┌───────────────────────────────────┐   │   │
│  │  │  Semantic Kernel  │  │  Tool Plugins                      │   │   │
│  │  │  + Function Call  │  │  ├─ CoreToolsPlugin                │   │   │
│  │  │    Behavior Auto  │  │  │   ├─ execute_command            │   │   │
│  │  └───────────────────┘  │  │   ├─ write_to_file              │   │   │
│  │                          │  │   └─ read_file                  │   │   │
│  │                          │  ├─ McpManagementPlugin            │   │   │
│  │                          │  │   ├─ search_mcp_servers         │   │   │
│  │                          │  │   └─ fetch_mcp_server_readme    │   │   │
│  │                          │  └─ McpToolsPlugin (dynamic)       │   │   │
│  │                          │       └─ [server]_[tool_name]      │   │   │
│  │                          └───────────────────────────────────────┘   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                    McpServerManager                               │   │
│  │  ┌─────────────┐  ┌──────────────┐  ┌────────────────────────┐  │   │
│  │  │ McpConfig   │  │ FileWatcher  │  │ McpClient[]            │  │   │
│  │  │ Service     │  │ (mcp.json)   │  │ (connected servers)    │  │   │
│  │  └─────────────┘  └──────────────┘  └────────────────────────┘  │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                    MCP Servers Catalog                            │   │
│  │  mcp-servers-catalog.json - минимальные метаданные               │   │
│  │  (githubUrl, description, tags - всё остальное из README)        │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
                              │ Stdio/HTTP
                              ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         MCP Servers (external)                          │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌────────────────────────┐  │
│  │ context7 │  │ tavily   │  │filesystem│  │ custom user servers    │  │
│  └──────────┘  └──────────┘  └──────────┘  └────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Компоненты

### 1. MCP Servers Catalog (mcp-servers-catalog.json)

**Минимальный** каталог - только то, что нужно для поиска:

```json
{
  "servers": [
    {
      "id": "context7",
      "name": "Context7",
      "description": "Поиск документации библиотек и фреймворков. Позволяет находить актуальную документацию по любым библиотекам.",
      "githubUrl": "https://github.com/upstash/context7-mcp",
      "tags": ["documentation", "search", "libraries", "frameworks"]
    },
    {
      "id": "tavily",
      "name": "Tavily Search", 
      "description": "Веб-поиск с использованием Tavily API. Поиск информации в интернете.",
      "githubUrl": "https://github.com/tavily-ai/tavily-mcp",
      "tags": ["search", "web", "internet"]
    },
    {
      "id": "filesystem",
      "name": "Filesystem",
      "description": "Доступ к файловой системе. Чтение, запись и управление файлами и директориями.",
      "githubUrl": "https://github.com/modelcontextprotocol/servers",
      "tags": ["filesystem", "files", "directories"]
    },
    {
      "id": "github",
      "name": "GitHub",
      "description": "Работа с GitHub API. Управление репозиториями, issues, pull requests.",
      "githubUrl": "https://github.com/modelcontextprotocol/servers",
      "tags": ["github", "git", "repositories", "code"]
    },
    {
      "id": "playwright",
      "name": "Playwright Browser",
      "description": "Автоматизация браузера. Взаимодействие с веб-страницами, скриншоты, навигация.",
      "githubUrl": "https://github.com/microsoft/playwright-mcp",
      "tags": ["browser", "automation", "web", "scraping"]
    }
  ]
}
```

### 2. Tools для AI

#### 2.1 CoreToolsPlugin (стандартные tools для работы с системой)

| Tool | Описание | Параметры |
|------|----------|-----------|
| `execute_command` | Выполнение команд терминала | command: string, workingDirectory?: string |
| `write_to_file` | Создание/редактирование файлов | path: string, content: string |
| `read_file` | Чтение файлов | path: string |

#### 2.2 McpManagementPlugin (tools для управления MCP)

| Tool | Описание | Параметры |
|------|----------|-----------|
| `search_mcp_servers` | Поиск серверов в каталоге по описанию или тегам | query: string |
| `fetch_mcp_server_readme` | Загрузка README.md из репозитория сервера | githubUrl: string |

#### 2.3 McpToolsPlugin (динамический - tools из подключённых серверов)

Автоматически создаёт Kernel Functions из tools подключённых MCP серверов:
- Имя функции: `{serverId}_{toolName}` (например: `context7_search_docs`)
- Описание: из MCP tool description
- Параметры: из MCP tool inputSchema

**Semantic Kernel автоматически передаёт все descriptions в API запрос.**

### 3. McpServerManager

Управляет жизненным циклом MCP серверов:

```csharp
public interface IMcpServerManager
{
    // Управление серверами
    Task InitializeAsync();
    Task ConnectServerAsync(string serverId);
    Task DisconnectServerAsync(string serverId);
    Task RestartServerAsync(string serverId);
    
    // Получение tools
    IReadOnlyList<McpServerInfo> GetConnectedServers();
    IReadOnlyList<McpToolInfo> GetAllTools();
    
    // Вызов tools
    Task<McpToolResult> InvokeToolAsync(string serverId, string toolName, JsonElement arguments);
    
    // События
    event EventHandler<McpServerChangedEventArgs> ServerChanged;
}
```

### 4. McpConfigurationService

Управление файлом конфигурации mcp.json:

```csharp
public interface IMcpConfigurationService
{
    string ConfigFilePath { get; }
    
    Task<McpConfiguration> LoadAsync();
    Task SaveAsync(McpConfiguration config);
    
    Task AddServerAsync(McpServerConfig serverConfig);
    Task RemoveServerAsync(string serverId);
    Task UpdateServerAsync(string serverId, McpServerConfig serverConfig);
    
    // File Watcher - автоматическое переподключение при изменении
    event EventHandler<McpConfigChangedEventArgs> ConfigurationChanged;
}
```

### 5. Формат конфигурации (mcp.json)

```json
{
  "mcpServers": {
    "context7": {
      "command": "npx",
      "args": ["-y", "@upstash/context7-mcp"],
      "type": "stdio",
      "enabled": true
    },
    "tavily": {
      "command": "npx",
      "args": ["-y", "tavily-mcp"],
      "type": "stdio",
      "enabled": true,
      "env": {
        "TAVILY_API_KEY": "user-api-key-here"
      }
    },
    "custom-server": {
      "command": "node",
      "args": ["/path/to/server/build/index.js"],
      "type": "stdio",
      "enabled": true,
      "env": {
        "API_KEY": "secret"
      }
    }
  }
}
```

---

## Алгоритм самостоятельной установки

### Сценарий 1: Пользователь просит выполнить задачу (простой сервер через npx)

```
1. Пользователь: "Найди документацию по Semantic Kernel"

2. AI анализирует задачу:
   - Нужен доступ к документации библиотек
   - Нет подходящего MCP tool
   
3. AI вызывает search_mcp_servers(query: "documentation libraries"):
   → Находит: context7 - "Поиск документации библиотек"
   
4. AI вызывает fetch_mcp_server_readme(githubUrl: "https://github.com/upstash/context7-mcp"):
   → Получает README с инструкциями
   
5. AI анализирует README и определяет:
   - Установка через npx (не требует клонирования)
   - Не требует API ключей
   - Формат конфигурации

6. AI выполняет установку:
   a) read_file("~/.desktopasst/mcp.json") - читает текущую конфигурацию
   b) write_to_file("~/.desktopasst/mcp.json") - добавляет сервер

7. FileWatcher обнаруживает изменение, McpServerManager подключает сервер

8. AI вызывает context7_search_docs для выполнения исходной задачи

9. AI предоставляет результат пользователю
```

### Сценарий 2: Установка сервера требующего клонирования и сборки

```
1. Пользователь: "Мне нужен доступ к браузеру"

2. AI находит playwright через search_mcp_servers

3. AI вызывает fetch_mcp_server_readme:
   → README указывает что нужно клонировать и собрать

4. AI выполняет установку:
   a) execute_command("git clone https://github.com/microsoft/playwright-mcp ~/.desktopasst/mcp-servers/playwright")
   b) execute_command("cd ~/.desktopasst/mcp-servers/playwright && npm install")
   c) execute_command("cd ~/.desktopasst/mcp-servers/playwright && npm run build")
   d) read_file("~/.desktopasst/mcp.json")
   e) write_to_file("~/.desktopasst/mcp.json") - добавляет:
      {
        "playwright": {
          "command": "node",
          "args": ["~/.desktopasst/mcp-servers/playwright/build/index.js"],
          "type": "stdio"
        }
      }

5. Сервер подключается автоматически
```

### Сценарий 3: Установка сервера с API ключом

```
1. Пользователь: "Мне нужен веб-поиск"

2. AI находит tavily через search_mcp_servers

3. AI вызывает fetch_mcp_server_readme:
   → README указывает что требуется TAVILY_API_KEY

4. AI отвечает пользователю:
   "Для работы Tavily Search требуется API ключ.
    Получите его на https://tavily.com и пришлите мне."
    
5. Пользователь отправляет следующее сообщение с ключом

6. AI выполняет установку с ключом в env:
   a) read_file("~/.desktopasst/mcp.json")
   b) write_to_file("~/.desktopasst/mcp.json") - добавляет сервер с env

7. Сервер подключается автоматически
```

### Сценарий 4: Ручная установка пользователем

```
1. Пользователь открывает Settings → MCP Servers

2. Нажимает "Add Server"

3. Вводит:
   - Name: my-server
   - Command: node
   - Args: /path/to/server/build/index.js
   - Environment variables

4. Система сохраняет конфигурацию и подключается
```

---

## Интеграция с Semantic Kernel

### Регистрация MCP Tools как Kernel Functions

```csharp
public class McpToolsPlugin
{
    private readonly IMcpServerManager _serverManager;
    
    public void RegisterToolsToKernel(Kernel kernel)
    {
        foreach (var server in _serverManager.GetConnectedServers())
        {
            foreach (var tool in server.Tools)
            {
                var functionName = $"{server.Id}_{tool.Name}";
                var function = CreateKernelFunction(server.Id, tool);
                kernel.Plugins.AddFromFunctions("MCP", [function]);
            }
        }
    }
    
    private KernelFunction CreateKernelFunction(string serverId, McpToolInfo tool)
    {
        return KernelFunctionFactory.CreateFromMethod(
            async (KernelArguments args) =>
            {
                var arguments = ConvertToJsonElement(args);
                var result = await _serverManager.InvokeToolAsync(serverId, tool.Name, arguments);
                return result.Content;
            },
            functionName: $"{serverId}_{tool.Name}",
            description: tool.Description,  // Semantic Kernel передаст это в API
            parameters: ConvertSchemaToParameters(tool.InputSchema)
        );
    }
}
```

### Function Calling в ChatService

```csharp
public async Task<string> SendMessageAsync(string message)
{
    var settings = new OpenAIPromptExecutionSettings
    {
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
    };
    
    // Semantic Kernel автоматически:
    // 1. Добавляет все tools с descriptions в API запрос
    // 2. Обрабатывает function calls от LLM
    // 3. Вызывает соответствующие functions
    // 4. Отправляет результаты обратно в LLM
    
    var result = await _kernel.InvokePromptAsync(message, new(settings));
    return result.ToString();
}
```

---

## Безопасность

1. **Подтверждение установки** - пользователь видит какие команды выполняются
2. **Изоляция** - MCP серверы работают в отдельных процессах
3. **Логирование** - все вызовы tools логируются
4. **Whitelist путей** - для filesystem сервера указываются разрешённые пути

---

## План реализации

### Шаг 1: Документирование стратегии ✓

Текущий документ.

### Шаг 2: Базовая MCP инфраструктура

1. Добавить NuGet пакет ModelContextProtocol
2. Создать модели: McpServerConfig, McpConfiguration, McpToolInfo, McpServerInfo
3. Создать IMcpConfigurationService + реализация
4. Создать IMcpServerManager + реализация
5. Добавить FileWatcher для mcp.json
6. Интегрировать с DI

### Шаг 3: Интеграция с Semantic Kernel

1. Создать McpToolsPlugin для динамической регистрации tools
2. Обновить KernelFactory для поддержки plugins
3. Обновить ChatService для function calling (FunctionChoiceBehavior.Auto)
4. Создать механизм обновления tools при изменении подключённых серверов

### Шаг 4: Tools для самоустановки

1. Создать mcp-servers-catalog.json
2. Создать CoreToolsPlugin (execute_command, write_to_file, read_file)
3. Создать McpManagementPlugin (search_mcp_servers, fetch_mcp_server_readme)
4. Реализовать загрузку README с GitHub API

### Шаг 5: UI для MCP (опционально)

1. MCP Settings View - список серверов
2. Добавление/удаление серверов вручную
3. Просмотр доступных tools

---

## Файловая структура

```
src/
├── DesktopAssistant.Application/
│   └── Interfaces/
│       ├── IMcpConfigurationService.cs
│       └── IMcpServerManager.cs
│
├── DesktopAssistant.Infrastructure/
│   └── MCP/
│       ├── Models/
│       │   ├── McpServerConfig.cs
│       │   ├── McpConfiguration.cs
│       │   ├── McpToolInfo.cs
│       │   ├── McpServerInfo.cs
│       │   └── McpCatalogEntry.cs
│       ├── Services/
│       │   ├── McpConfigurationService.cs
│       │   └── McpServerManager.cs
│       ├── Plugins/
│       │   ├── CoreToolsPlugin.cs
│       │   ├── McpManagementPlugin.cs
│       │   └── McpToolsPlugin.cs
│       └── Resources/
│           └── mcp-servers-catalog.json
│
└── DesktopAssistant.UI/
    └── Views/
        └── McpSettingsView.axaml (optional)
```

---

## Пути к файлам

- Конфигурация MCP: `~/.desktopasst/mcp.json`
- Клонированные серверы: `~/.desktopasst/mcp-servers/`
- Каталог серверов: embedded resource в приложении

---

## Зависимости

```xml
<PackageReference Include="ModelContextProtocol" Version="0.2.0-preview.*" />
```

---

## Метрики успеха

- [ ] AI может найти подходящий MCP сервер для задачи через search_mcp_servers
- [ ] AI может загрузить README сервера через fetch_mcp_server_readme
- [ ] AI может самостоятельно установить MCP сервер (включая клонирование если нужно)
- [ ] AI может запросить у пользователя API ключ через обычное сообщение в чате
- [ ] AI может использовать tools установленных серверов
- [ ] Конфигурация сохраняется между сессиями
- [ ] Пользователь может вручную добавить MCP сервер
- [ ] FileWatcher автоматически подключает новые серверы при изменении mcp.json
