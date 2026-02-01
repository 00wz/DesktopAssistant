# Архитектура самостоятельной установки MCP-серверов AI-ассистентом

## Общая концепция

AI-ассистент способен самостоятельно устанавливать MCP (Model Context Protocol) серверы по запросу пользователя. Ключевая идея: **вместо специализированного механизма установки используется обычная задача (task) с инструкциями для AI**, который выполняет её с помощью стандартных инструментов.

---

## 1. Необходимые Tools для AI-ассистента

### 1.1. MCP-специфичные tools

| Tool | Параметры | Описание |
|------|-----------|----------|
| `load_mcp_documentation` | нет | Загружает документацию по созданию MCP серверов |
| `use_mcp_tool` | server_name, tool_name, arguments (JSON) | Вызывает инструмент на подключённом сервере |
| `access_mcp_resource` | server_name, uri | Читает ресурс с сервера |

### 1.2. Стандартные tools (необходимы для установки)

| Tool | Описание | Использование при установке |
|------|----------|----------------------------|
| `execute_command` | Выполнение команд терминала | Создание проекта, npm install, сборка, git clone |
| `write_to_file` | Создание/перезапись файлов | Код сервера, редактирование конфигурации |
| `read_file` | Чтение файлов | Чтение текущей конфигурации |
| `ask_followup_question` | Запрос информации у пользователя | Получение API ключей |

---

## 2. Алгоритм установки MCP-сервера

### 2.1. Способ 1: Через маркетплейс

```
1. Пользователь выбирает сервер в UI каталога
   
2. Система загружает метаданные с API маркетплейса:
   - githubUrl: URL репозитория
   - readmeContent: содержимое README проекта
   - llmsInstallationContent: специальные инструкции для AI
   - requiresApiKey: нужен ли API ключ
   
3. Система создаёт задачу для AI с промптом:
   
   TASK_PROMPT = """
   Set up the MCP server from {githubUrl} while adhering to these MCP server
   installation rules:
   - Start by loading the MCP documentation.
   - Use "{mcpId}" as the server name in cline_mcp_settings.json.
   - Create the directory for the new MCP server before starting installation.
   - Make sure you read the user's existing cline_mcp_settings.json file before
     editing it with this new mcp, to not overwrite any existing servers.
   - Use commands aligned with the user's shell and operating system best practices.
   - The following README may contain instructions that conflict with the user's OS,
     in which case proceed thoughtfully.
   - Once installed, demonstrate the server's capabilities by using one of its tools.
   
   Here is the project's README to help you get started:
   {readmeContent}
   {llmsInstallationContent}
   """
   
4. AI выполняет задачу, следуя инструкциям из промпта.
   Типичные действия (порядок определяет AI):
   - Клонирует/скачивает репозиторий (execute_command)
   - Устанавливает зависимости (execute_command: npm install)
   - Собирает проект (execute_command: npm run build)
   - Запрашивает API ключи у пользователя если нужно (ask_followup_question)
   - Читает текущую конфигурацию (read_file)
   - Добавляет новый сервер в конфигурацию (write_to_file)
   - Демонстрирует работу (use_mcp_tool)

5. Система автоматически подключается к новому серверу
   (file watcher обнаруживает изменение конфигурации)
```

### 2.2. Способ 2: По запросу пользователя

```
1. Пользователь запрашивает: "Добавь инструмент для получения погоды"

2. AI вызывает load_mcp_documentation

3. AI получает документацию с:
   - Путём для создания серверов
   - Командой инициализации проекта
   - Примером кода MCP сервера
   - Форматом конфигурации

4. AI выполняет создание:
   a) Создаёт проект (execute_command):
      cd {servers_path}
      npx @modelcontextprotocol/create-server weather-server
      cd weather-server
      npm install axios
      
   b) Пишет код сервера (write_to_file):
      Создаёт src/index.ts с логикой сервера
      
   c) Собирает проект (execute_command):
      npm run build
      
   d) Запрашивает API ключ (ask_followup_question):
      "Для работы сервера нужен API ключ OpenWeather..."
      
   e) Читает конфигурацию (read_file)
   
   f) Добавляет сервер в конфигурацию (write_to_file)

5. Система автоматически подключается

6. AI предлагает использовать новые возможности
```

---

## 3. Два режима работы с MCP tools

### 3.1. Режим Native Tool Calls (для современных моделей)

Когда LLM поддерживает native tool calling (GPT-5, Claude 3.5+, Gemini 3+), **каждый MCP tool регистрируется как отдельный tool** в API запросе.

**Формирование имени tool:**
```
toolName = server.uid + IDENTIFIER + mcpTool.name
         = "c1234F" + "0mcp0" + "get_forecast"
         = "c1234F0mcp0get_forecast"
```

**Структура:**
- `server.uid` - короткий уникальный ID сервера (6 символов)
- `IDENTIFIER` = "0mcp0" - разделитель для парсинга
- `mcpTool.name` - имя инструмента

**Ограничение:** Общая длина имени ≤ 64 символа (ограничение API провайдеров)

**Регистрация в API запросе:**
```json
{
  "tools": [
    { "name": "read_file", "description": "...", "parameters": {...} },
    { 
      "name": "c1234F0mcp0get_forecast",
      "description": "weather-server: Get weather forecast for a city",
      "parameters": {
        "type": "object",
        "properties": {
          "city": { "type": "string", "description": "City name" },
          "days": { "type": "number", "description": "Number of days" }
        },
        "required": ["city"]
      }
    }
  ]
}
```

**Парсинг вызова (восстановление server + tool):**
```
toolName = "c1234F0mcp0get_forecast"
parts = toolName.split("0mcp0")      // ["c1234F", "get_forecast"]
serverKey = parts[0]                  // "c1234F"
mcpToolName = parts[1]                // "get_forecast"
serverName = getServerByKey(serverKey) // "weather-server"
```

**Преимущества:**
- LLM "видит" каждый MCP tool как обычный tool
- Полная JSON Schema параметров доступна LLM
- Более точные вызовы

### 3.2. Режим XML (для моделей без native tools)

Используется **один tool `use_mcp_tool`** с параметрами:

```xml
<use_mcp_tool>
<server_name>weather-server</server_name>
<tool_name>get_forecast</tool_name>
<arguments>
{
  "city": "San Francisco",
  "days": 5
}
</arguments>
</use_mcp_tool>
```

Информация о доступных инструментах предоставляется в системном промпте.

---

## 4. Системный промпт - секция MCP

AI должен видеть информацию о подключённых серверах:

```
MCP SERVERS

The Model Context Protocol (MCP) enables communication with locally running 
servers that provide additional tools and resources.

# Connected MCP Servers

## weather-server

### Available Tools
- get_forecast: Get weather forecast for a city
  Input Schema:
  {
    "type": "object",
    "properties": {
      "city": { "type": "string", "description": "City name" },
      "days": { "type": "number", "description": "Number of days (1-5)" }
    },
    "required": ["city"]
  }

### Resource Templates
- weather://{city}/current: Real-time weather data for a city
```

---

## 5. Документация для AI (load_mcp_documentation)

Содержимое документации должно включать:

```
## Creating an MCP Server

MCP servers operate in a non-interactive environment. All credentials 
must be provided through environment variables in the config file.

Default path for new servers: {MCP_SERVERS_PATH}

### Steps:

1. Create project:
   cd {MCP_SERVERS_PATH}
   npx @modelcontextprotocol/create-server my-server
   cd my-server
   npm install dependencies

2. Implement server in src/index.ts using MCP SDK

3. Build:
   npm run build

4. Get required API keys from user using ask_followup_question

5. Add to config file:
   {
     "mcpServers": {
       "my-server": {
         "command": "node",
         "args": ["/path/to/my-server/build/index.js"],
         "env": { "API_KEY": "..." }
       }
     }
   }

6. System will automatically connect

### Example Server Code:

[Полный пример TypeScript MCP сервера ~250 строк]
```

---

## 6. Структура данных

### McpServer
```
{
  name: string                           // Уникальное имя сервера
  status: "connected" | "connecting" | "disconnected"
  tools: McpTool[]                       // Доступные инструменты
  resources: McpResource[]               // Доступные ресурсы
  resourceTemplates: McpResourceTemplate[] // Шаблоны ресурсов
  disabled?: boolean                     // Отключён ли сервер
}
```

### McpTool
```
{
  name: string              // Имя инструмента
  description?: string      // Описание
  inputSchema?: object      // JSON Schema параметров
}
```

### Конфигурация сервера (stdio - локальный)
```json
{
  "command": "node",
  "args": ["/path/to/server/build/index.js"],
  "env": { "API_KEY": "..." },
  "disabled": false
}
```

### Конфигурация сервера (remote)
```json
{
  "type": "sse" | "streamableHttp",
  "url": "https://example.com/mcp",
  "headers": { "Authorization": "Bearer ..." }
}
```

---

## 7. Диаграмма потока данных

```
┌──────────────────────────────────────────────────────────────────┐
│                        User Request                               │
│  "Установи MCP сервер погоды" / Выбор в маркетплейсе            │
└───────────────────────────────┬──────────────────────────────────┘
                                │
                                ▼
┌──────────────────────────────────────────────────────────────────┐
│                      Task Creation                                │
│  Формирование задачи с инструкциями для AI                       │
│  (включая README если из маркетплейса)                           │
└───────────────────────────────┬──────────────────────────────────┘
                                │
                                ▼
┌──────────────────────────────────────────────────────────────────┐
│                       AI Execution                                │
│                                                                  │
│  1. load_mcp_documentation  ──►  Получить инструкции             │
│  2. execute_command         ──►  Создать/клонировать проект      │
│  3. write_to_file           ──►  Написать код сервера            │
│  4. execute_command         ──►  Установить зависимости          │
│  5. execute_command         ──►  Собрать проект                  │
│  6. ask_followup_question   ──►  Получить API ключи              │
│  7. read_file               ──►  Прочитать текущую конфиг        │
│  8. write_to_file           ──►  Добавить сервер в конфиг        │
│  9. use_mcp_tool            ──►  Демонстрация работы             │
└───────────────────────────────┬──────────────────────────────────┘
                                │
                                ▼
┌──────────────────────────────────────────────────────────────────┐
│                 Config File Monitoring                            │
│                                                                  │
│  File Watcher ──► Обнаружение изменения конфигурации            │
│        │                                                         │
│        ▼                                                         │
│  Подключение к новому серверу                                    │
│        │                                                         │
│        ▼                                                         │
│  Загрузка tools, resources, templates                            │
│        │                                                         │
│        ▼                                                         │
│  Обновление системного промпта                                   │
└──────────────────────────────────────────────────────────────────┘
```

---

## 8. Ключевые принципы

1. **Задача как механизм установки**
   - Нет специального tool для установки
   - Создаётся обычная задача с инструкциями
   - AI использует стандартные tools

2. **Документация по запросу**
   - load_mcp_documentation предоставляет все знания
   - Содержит примеры кода и формат конфигурации

3. **Автоматическое подключение**
   - File watcher отслеживает конфигурацию
   - При изменении - автоматическое переподключение
   - Информация обновляется в системном промпте

4. **Два режима вызова MCP tools**
   - Native: каждый MCP tool = отдельный tool в API
   - XML: один `use_mcp_tool` с параметрами

5. **Безопасность**
   - Вызовы MCP tools требуют подтверждения пользователя

---

## 9. Универсальный tool для добавления MCP серверов

### 9.1. add_mcp_server

Универсальный tool для добавления любого MCP сервера в конфигурацию:

```csharp
add_mcp_server(
    serverId: string,   // Уникальный ID
    command: string,    // 'npx', 'node', 'python' и т.д.
    argsJson: string,   // JSON массив аргументов
    envJson: string     // JSON объект переменных окружения
)
```

**Примеры использования:**

1. **npx сервер** (публикуется в npm):
```
add_mcp_server(
    serverId: "tavily",
    command: "npx",
    argsJson: "["-y", "tavily-mcp@0.2.1"]",
    envJson: "{"TAVILY_API_KEY": "tvly-xxx"}"
)
```

2. **node сервер** (после клонирования и сборки):
```
add_mcp_server(
    serverId: "weather",
    command: "node",
    argsJson: "["/home/user/.desktopasst/mcp-servers/weather/build/index.js"]",
    envJson: "{"API_KEY": "xxx"}"
)
```

3. **python сервер**:
```
add_mcp_server(
    serverId: "custom",
    command: "python",
    argsJson: "["/path/to/server/main.py"]",
    envJson: "{}"
)
```

### 9.2. Автоматические действия

1. **Для npx команд** - валидация npm пакета через `npm view`
2. Запись конфигурации в mcp.json
3. Ожидание автоматического подключения (FileWatcher)
4. Возврат результата:
   - ✅ Успех: список доступных tools
   - ❌ Ошибка валидации npm (только для npx)
   - ❌ Ошибка подключения с диагностикой
   - ⚠️ Статус неизвестен

### 9.3. Алгоритм установки серверов

**Простой сервер (npx):**
```
1. fetch_mcp_server_readme(githubUrl)
   ↓
2. add_mcp_server(serverId, "npx", argsJson, envJson)
   ↓
   Готово! Tools доступны.
```

**Сервер требующий сборки:**
```
1. fetch_mcp_server_readme(githubUrl)
   ↓
2. execute_command("git clone ... {get_mcp_servers_directory()}/server-name")
   ↓
3. execute_command("cd ... && npm install && npm run build")
   ↓
4. add_mcp_server(serverId, "node", "[path/to/build/index.js]", envJson)
   ↓
   Готово! Tools доступны.
```

### 9.4. Доступные MCP management tools

| Tool | Описание |
|------|----------|
| `search_mcp_servers` | Поиск серверов в каталоге |
| `fetch_mcp_server_readme` | Загрузка README из GitHub |
| `add_mcp_server` | Универсальное добавление сервера (с автоматической валидацией) |
| `get_mcp_config_path` | Путь к mcp.json (информационный) |
| `get_mcp_servers_directory` | Путь для клонирования серверов |

### 9.5. Примеры результатов add_mcp_server

**Успех:**
```
✅ MCP СЕРВЕР 'tavily' УСПЕШНО УСТАНОВЛЕН И ПОДКЛЮЧЕН!

Конфигурация:
  Command: npx
  Args: -y tavily-mcp@0.2.1
  Env: 1 переменных

Доступные tools (2):
  - tavily_search: Search the web using Tavily API
  - tavily_extract: Extract content from URLs
```

**Ошибка валидации npm (только для npx):**
```
❌ ОШИБКА ВАЛИДАЦИИ npm пакета!

Пакет: @tavily/mcp-server-tavily
Ошибка: Пакет не найден в npm registry

РЕШЕНИЕ: Вернись к README и найди ТОЧНОЕ имя пакета!
```

**Ошибка подключения:**
```
❌ ОШИБКА ПОДКЛЮЧЕНИЯ к MCP серверу 'weather'!

Ошибка: Cannot find module '/path/to/build/index.js'

РЕШЕНИЕ: Проверь что сервер собран и путь указан верно.
```

### 9.6. Универсальная валидация серверов

`add_mcp_server` выполняет **предварительную валидацию** перед записью конфигурации, адаптированную под тип сервера:

| Тип сервера | Команда | Проверка |
|-------------|---------|----------|
| **npx** | `npx` | Проверка npm пакета через `npm view` |
| **node** | `node` | Проверка существования .js/.mjs файла |
| **python** | `python`, `python3` | Проверка существования .py файла |
| **Другие** | любая | Проверка доступности команды в PATH (`where`/`which`) |

**Логика валидации:**

```csharp
ValidateServerConfigAsync(command, args):
    1. Если command == "npx":
       - Найти имя пакета в args (первый аргумент без "-")
       - Выполнить: npm view {package} name --json
       - При ошибке: вернуть "❌ ОШИБКА ВАЛИДАЦИИ npm пакета!"
       
    2. Если command == "node" / "python" / "python3":
       - Найти путь к скрипту в args (.js, .mjs, .py)
       - Проверить: File.Exists(scriptPath)
       - При отсутствии: вернуть "❌ ОШИБКА: Файл скрипта не найден!"
       
    3. Для других команд:
       - Проверить: where/which {command}
       - При отсутствии: вернуть "⚠️ ПРЕДУПРЕЖДЕНИЕ: Команда может быть недоступна"
       
    4. Если все проверки пройдены: return null (продолжить установку)
```

**Примеры ошибок:**

```
❌ ОШИБКА ВАЛИДАЦИИ npm пакета!

Пакет: @tavily/mcp-server
Ошибка: Пакет не найден в npm registry

Возможные причины:
1. Имя пакета написано с ошибкой
2. Ты ПРИДУМАЛ имя пакета вместо копирования из README

РЕШЕНИЕ: Вернись к README (fetch_mcp_server_readme) и найди ТОЧНОЕ имя пакета!
```

```
❌ ОШИБКА: Файл скрипта не найден!

Путь: /home/user/.desktopasst/mcp-servers/weather/build/index.js

Возможные причины:
1. Сервер не был склонирован или не собран
2. Путь указан неверно

РЕШЕНИЕ:
1. Убедись что репозиторий склонирован в get_mcp_servers_directory()
2. Выполни сборку: execute_command('npm install && npm run build')
3. Проверь правильность пути к собранному файлу
```

```
⚠️ ПРЕДУПРЕЖДЕНИЕ: Команда 'uvx' может быть недоступна в системе.

Конфигурация будет записана, но сервер может не запуститься.
Убедись что программа установлена и доступна в PATH.
```

### 9.7. Поддерживаемые типы MCP серверов

| Тип | Команда | Аргументы | Пример |
|-----|---------|-----------|--------|
| **npm (npx)** | `npx` | `["-y", "package@version"]` | `["npx", ["-y", "tavily-mcp@0.2.1"]]` |
| **npm (npx scoped)** | `npx` | `["-y", "@scope/package"]` | `["npx", ["-y", "@upstash/context7-mcp"]]` |
| **Node.js** | `node` | `["/path/to/index.js"]` | `["node", ["/path/build/index.js"]]` |
| **Python** | `python` / `python3` | `["/path/to/main.py"]` | `["python", ["-m", "server"]]` |
| **uv (Python)** | `uvx` | `["package"]` | `["uvx", ["mcp-server-fetch"]]` |
| **Docker** | `docker` | `["run", "-i", "image"]` | `["docker", ["run", "-i", "mcp/server"]]` |
| **Другие** | любая | любые | Зависит от сервера |
