# DesktopAssistant

Кроссплатформенный десктопный AI-агент на .NET с поддержкой голосового ввода/вывода.

## 🎯 Возможности

- **Множественные диалоги** — работа с несколькими AI-ассистентами одновременно через вкладки
- **Ветвящаяся история** — граф диалога с возможностью создания альтернативных веток (как в ChatGPT)
- **Голосовой ввод** — стриминговое распознавание речи с помощью Vosk (офлайн)
- **Wake Word** — активация ассистента голосовой командой
- **Голосовой вывод** — стриминговое преобразование ответов AI в речь
- **MCP-серверы** — поддержка Model Context Protocol для расширения возможностей
- **Работа в трее** — фоновый режим с ожиданием wake word
- **Автоматическая суммаризация** — при достижении лимита контекста

## 🛠 Технологический стек

| Компонент | Технология |
|-----------|------------|
| UI Framework | [Avalonia UI](https://avaloniaui.net/) 11.3 |
| MVVM | [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) 8.4 |
| AI Orchestration | [Semantic Kernel](https://learn.microsoft.com/semantic-kernel) 1.32 |
| Speech Recognition | [Vosk](https://alphacephei.com/vosk/) 0.3 |
| Text-to-Speech | Azure Speech / OpenAI TTS |
| Database | SQLite + EF Core 9 |
| Audio | NAudio 2.2 |

## 📋 Требования

- .NET 9.0 SDK
- Windows 10/11, macOS 12+, или Linux

## 🚀 Быстрый старт

### Клонирование репозитория

```bash
git clone https://github.com/yourusername/DesktopAssistant.git
cd DesktopAssistant
```

### Сборка и запуск

```bash
dotnet restore
dotnet build
dotnet run --project src/DesktopAssistant.UI
```

### Первоначальная настройка

При первом запуске приложение предложит:
1. Выбрать LLM провайдера (OpenAI, Azure OpenAI, и др.)
2. Выбрать модель
3. Ввести API-ключ

## 📁 Структура проекта

```
DesktopAssistant/
├── src/
│   ├── DesktopAssistant.Domain/        # Доменный слой (сущности, интерфейсы)
│   ├── DesktopAssistant.Application/   # Слой приложения (сервисы, use cases)
│   ├── DesktopAssistant.Infrastructure/# Инфраструктура (БД, AI, Speech)
│   └── DesktopAssistant.UI/            # Презентационный слой (Avalonia)
├── tests/                              # Тесты
├── docs/                               # Документация
└── memory-bank/                        # Документация проекта для AI
```

## 🔧 Конфигурация

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=desktopassistant.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

### User Secrets (API ключи)

```bash
cd src/DesktopAssistant.UI
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
dotnet user-secrets set "Azure:SpeechKey" "..."
dotnet user-secrets set "Azure:SpeechRegion" "eastus"
```

## 🗣 Голосовые функции

### Vosk (Speech-to-Text)

Для работы распознавания речи необходимо скачать языковую модель:

1. Скачайте модель с https://alphacephei.com/vosk/models
2. Распакуйте в папку `models/vosk/`
3. Укажите путь в настройках приложения

### TTS (Text-to-Speech)

Поддерживаются провайдеры:
- Azure Cognitive Services Speech
- OpenAI TTS API

## 🔌 MCP-серверы

Приложение поддерживает подключение MCP-серверов для расширения возможностей AI.

Пример конфигурации `mcp-servers.json`:

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@anthropic-ai/mcp-filesystem-server", "/path/to/allowed/dir"]
    }
  }
}
```

## 📝 Roadmap

- [x] Базовая архитектура Clean Architecture
- [x] Структура проекта и зависимости
- [ ] UI с вкладками диалогов
- [ ] Интеграция с LLM провайдерами
- [ ] Сохранение диалогов в SQLite
- [ ] Ветвящаяся история диалогов
- [ ] Голосовой ввод (Vosk)
- [ ] Голосовой вывод (TTS)
- [ ] Wake word детекция
- [ ] MCP интеграция
- [ ] Работа в системном трее
- [ ] Автоматическая суммаризация

## 📄 Лицензия

MIT License

## 🤝 Вклад в проект

Приветствуются pull requests и issues!
