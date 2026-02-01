using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Infrastructure.MCP.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Infrastructure.MCP.Plugins;

/// <summary>
/// Плагин с tools для управления MCP серверами (поиск, установка)
/// </summary>
public class McpManagementPlugin
{
    private readonly ILogger<McpManagementPlugin> _logger;
    private readonly HttpClient _httpClient;
    private readonly IMcpServerManager _serverManager;
    private readonly IMcpConfigurationService _configService;
    private McpServersCatalog? _catalog;
    
    public McpManagementPlugin(
        ILogger<McpManagementPlugin> logger,
        IMcpServerManager serverManager,
        IMcpConfigurationService configService,
        HttpClient? httpClient = null)
    {
        _logger = logger;
        _serverManager = serverManager;
        _configService = configService;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DesktopAssistant", "1.0"));
    }
    
    /// <summary>
    /// Ищет MCP серверы в каталоге по описанию или тегам
    /// </summary>
    [KernelFunction("search_mcp_servers")]
    [Description("Поиск MCP серверов в каталоге по описанию задачи или ключевым словам. Возвращает список подходящих серверов с GitHub URL. ВАЖНО: После нахождения сервера ОБЯЗАТЕЛЬНО загрузи README через fetch_mcp_server_readme для получения ТОЧНОЙ команды установки.")]
    public async Task<string> SearchMcpServersAsync(
        [Description("Поисковый запрос: описание задачи или ключевые слова")] string query)
    {
        var catalog = await LoadCatalogAsync();
        
        if (catalog.Servers.Count == 0)
        {
            return "Каталог MCP серверов пуст.";
        }
        
        // Простой поиск по вхождению слов в описание и теги
        var queryWords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        var results = catalog.Servers
            .Select(s => new
            {
                Server = s,
                Score = CalculateRelevanceScore(s, queryWords)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(5)
            .ToList();
        
        if (results.Count == 0)
        {
            return $"Не найдено MCP серверов по запросу '{query}'. Попробуйте другие ключевые слова.";
        }
        
        var resultText = $"Найдено {results.Count} MCP серверов:\n\n";
        
        foreach (var item in results)
        {
            var s = item.Server;
            resultText += $"**{s.Name}** (id: {s.Id})\n";
            resultText += $"Описание: {s.Description}\n";
            resultText += $"GitHub: {s.GitHubUrl}\n";
            resultText += $"Теги: {string.Join(", ", s.Tags)}\n\n";
        }
        
        resultText += "\nДля установки загрузи README через fetch_mcp_server_readme и следуй инструкциям.\n";
        
        return resultText;
    }
    
    /// <summary>
    /// Загружает README.md из репозитория MCP сервера
    /// </summary>
    [KernelFunction("fetch_mcp_server_readme")]
    [Description("Загружает README.md из GitHub репозитория MCP сервера. README содержит ТОЧНУЮ команду установки (npx ...) - используй ИМЕННО её, не изменяя имя пакета! Ищи строки вида 'npx -y package-name' или секцию с конфигурацией.")]
    public async Task<string> FetchMcpServerReadmeAsync(
        [Description("URL GitHub репозитория (например: https://github.com/upstash/context7-mcp)")] string githubUrl)
    {
        try
        {
            // Преобразуем GitHub URL в raw URL для README
            var rawUrl = ConvertToRawReadmeUrl(githubUrl);
            
            var response = await _httpClient.GetAsync(rawUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                // Пробуем альтернативные имена
                var altUrls = new[]
                {
                    rawUrl.Replace("README.md", "readme.md"),
                    rawUrl.Replace("README.md", "Readme.md"),
                    rawUrl.Replace("/main/", "/master/"),
                    rawUrl.Replace("/main/", "/master/").Replace("README.md", "readme.md"),
                };
                
                foreach (var altUrl in altUrls)
                {
                    response = await _httpClient.GetAsync(altUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        break;
                    }
                }
            }
            
            if (!response.IsSuccessStatusCode)
            {
                return $"Не удалось загрузить README из {githubUrl}. Код ответа: {response.StatusCode}";
            }
            
            var readme = await response.Content.ReadAsStringAsync();
            
            return $"README из {githubUrl}:\n\n{readme}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching README from {Url}", githubUrl);
            return $"Ошибка при загрузке README: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Возвращает путь к файлу конфигурации MCP
    /// </summary>
    [KernelFunction("get_mcp_config_path")]
    [Description("Возвращает путь к файлу конфигурации MCP серверов (mcp.json). Используй для информации - для добавления сервера лучше использовать add_mcp_server.")]
    public string GetMcpConfigPath()
    {
        return _configService.ConfigFilePath;
    }
    
    /// <summary>
    /// Возвращает путь для клонирования MCP серверов
    /// </summary>
    [KernelFunction("get_mcp_servers_directory")]
    [Description("Возвращает путь к директории для клонирования MCP серверов")]
    public string GetMcpServersDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".desktopasst",
            "mcp-servers");
    }
    
    /// <summary>
    /// Универсальный tool для добавления MCP сервера в конфигурацию.
    /// Работает с любыми серверами: npx, node, python и т.д.
    /// </summary>
    [KernelFunction("add_mcp_server")]
    [Description(@"Добавляет MCP сервер в конфигурацию и пытается подключиться.
Работает для ЛЮБЫХ серверов: npx, node, python и т.д.

Примеры:
- npx сервер: command='npx', args='[""-y"", ""tavily-mcp@0.2.1""]'
- node сервер: command='node', args='[""/path/to/server/build/index.js""]'
- python сервер: command='python', args='[""/path/to/server/main.py""]'

Автоматически:
1. Для npx - проверяет существование npm пакета
2. Записывает конфигурацию в mcp.json
3. Пытается подключиться к серверу
4. Возвращает результат с доступными tools или ошибкой

ВАЖНО: Для npx серверов имя пакета должно быть ТОЧНО из README!")]
    public async Task<string> AddMcpServerAsync(
        [Description("Уникальный ID сервера для конфигурации (например: 'tavily', 'weather-server')")] string serverId,
        [Description("Команда для запуска: 'npx', 'node', 'python' и т.д.")] string command,
        [Description("Аргументы командной строки в формате JSON массива")] string argsJson,
        [Description("Переменные окружения в формате JSON объекта. Может быть пустым: {}")] string envJson)
    {
        try
        {
            // 1. Парсим аргументы
            List<string> args;
            try
            {
                args = JsonSerializer.Deserialize<List<string>>(argsJson) ?? new List<string>();
            }
            catch (JsonException ex)
            {
                return $"❌ ОШИБКА: Некорректный формат args JSON: {ex.Message}\n" +
                       "Формат должен быть JSON массивом: [\"-y\", \"package-name@version\"]";
            }
            
            // 2. Парсим env
            Dictionary<string, string> env;
            try
            {
                env = JsonSerializer.Deserialize<Dictionary<string, string>>(envJson) ?? new Dictionary<string, string>();
            }
            catch (JsonException ex)
            {
                return $"❌ ОШИБКА: Некорректный формат env JSON: {ex.Message}\n" +
                       "Формат должен быть JSON объектом: {\"KEY\": \"value\"}";
            }
            
            // 3. Универсальная валидация перед записью конфигурации
            var validationError = await ValidateServerConfigAsync(command, args);
            if (validationError != null)
            {
                return validationError;
            }
            
            // 4. Записываем конфигурацию
            var serverConfig = new McpServerConfigDto
            {
                Command = command,
                Args = args,
                Env = env,
                Enabled = true
            };
            
            await _configService.AddServerAsync(serverId, serverConfig);
            _logger.LogInformation("MCP server config written for '{ServerId}'", serverId);
            
            // 6. Ждём немного и пробуем подключиться
            await Task.Delay(500); // Даём время FileWatcher обработать изменение
            
            // 7. Проверяем статус подключения с несколькими попытками
            const int maxAttempts = 5;
            const int delayMs = 1000;
            
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var servers = _serverManager.GetConnectedServers();
                var connectedServer = servers.FirstOrDefault(s => s.Id == serverId);
                
                if (connectedServer != null && connectedServer.Status == McpServerStatusDto.Connected)
                {
                    // Успех! Формируем отчёт
                    var toolsList = connectedServer.Tools.Any()
                        ? string.Join("\n", connectedServer.Tools.Select(t => $"  - {t.Name}: {t.Description ?? "no description"}"))
                        : "  (нет tools)";
                    
                    return $"✅ MCP СЕРВЕР '{serverId}' УСПЕШНО УСТАНОВЛЕН И ПОДКЛЮЧЕН!\n\n" +
                           $"Конфигурация:\n" +
                           $"  Command: {command}\n" +
                           $"  Args: {string.Join(" ", args)}\n" +
                           (env.Any() ? $"  Env: {env.Keys.Count} переменных\n" : "") +
                           $"\nДоступные tools ({connectedServer.Tools.Count}):\n{toolsList}\n\n" +
                           "Теперь ты можешь использовать эти tools для выполнения задач пользователя.";
                }
                
                if (attempt < maxAttempts)
                {
                    await Task.Delay(delayMs);
                }
            }
            
            // 8. Не удалось подключиться - проверяем ошибку
            var allServers = _serverManager.GetConnectedServers();
            var serverInfo = allServers.FirstOrDefault(s => s.Id == serverId);
            
            if (serverInfo != null && serverInfo.Status == McpServerStatusDto.Error)
            {
                return $"❌ ОШИБКА ПОДКЛЮЧЕНИЯ к MCP серверу '{serverId}'!\n\n" +
                       $"Конфигурация записана, но сервер не запустился.\n" +
                       $"Ошибка: {serverInfo.ErrorMessage}\n\n" +
                       "Возможные причины:\n" +
                       "1. Неверный формат команды или аргументов\n" +
                       "2. Отсутствуют необходимые переменные окружения (API ключи)\n" +
                       "3. Пакет требует дополнительной настройки\n\n" +
                       "РЕШЕНИЕ: Проверь README и убедись что все параметры указаны верно.";
            }
            
            return $"⚠️ MCP сервер '{serverId}' - конфигурация записана, но статус подключения неизвестен.\n\n" +
                   "Конфигурация сохранена в mcp.json.\n" +
                   "Сервер может подключиться позже или требует перезапуска приложения.\n\n" +
                   "Если сервер не появился, проверь логи на наличие ошибок.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error installing MCP server '{ServerId}'", serverId);
            return $"❌ КРИТИЧЕСКАЯ ОШИБКА при установке MCP сервера: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Универсальная валидация конфигурации сервера перед записью
    /// </summary>
    private async Task<string?> ValidateServerConfigAsync(string command, List<string> args)
    {
        var commandLower = command.ToLowerInvariant();
        
        // 1. Для npx - валидируем npm пакет
        if (commandLower == "npx")
        {
            string? packageName = null;
            foreach (var arg in args)
            {
                if (!arg.StartsWith("-") && !arg.StartsWith("/"))
                {
                    packageName = arg;
                    break;
                }
            }
            
            if (!string.IsNullOrEmpty(packageName))
            {
                var validationResult = await ValidateNpmPackageInternalAsync(packageName);
                if (!validationResult.IsValid)
                {
                    return $"❌ ОШИБКА ВАЛИДАЦИИ npm пакета!\n\n" +
                           $"Пакет: {packageName}\n" +
                           $"Ошибка: {validationResult.Error}\n\n" +
                           "Возможные причины:\n" +
                           "1. Имя пакета написано с ошибкой\n" +
                           "2. Ты ПРИДУМАЛ имя пакета вместо копирования из README\n\n" +
                           "РЕШЕНИЕ: Вернись к README (fetch_mcp_server_readme) и найди ТОЧНОЕ имя пакета!";
                }
            }
            return null;
        }
        
        // 2. Для node/python - проверяем существование файла скрипта
        if (commandLower == "node" || commandLower == "python" || commandLower == "python3")
        {
            // Ищем путь к файлу в аргументах
            string? scriptPath = null;
            foreach (var arg in args)
            {
                if (!arg.StartsWith("-") && (arg.EndsWith(".js") || arg.EndsWith(".py") || arg.EndsWith(".mjs")))
                {
                    scriptPath = arg;
                    break;
                }
            }
            
            if (!string.IsNullOrEmpty(scriptPath) && !File.Exists(scriptPath))
            {
                return $"❌ ОШИБКА: Файл скрипта не найден!\n\n" +
                       $"Путь: {scriptPath}\n\n" +
                       "Возможные причины:\n" +
                       "1. Сервер не был склонирован или не собран\n" +
                       "2. Путь указан неверно\n\n" +
                       "РЕШЕНИЕ:\n" +
                       "1. Убедись что репозиторий склонирован в get_mcp_servers_directory()\n" +
                       "2. Выполни сборку: execute_command('npm install && npm run build')\n" +
                       "3. Проверь правильность пути к собранному файлу";
            }
            return null;
        }
        
        // 3. Проверяем что команда доступна в системе
        var commandExists = await CheckCommandExistsAsync(command);
        if (!commandExists)
        {
            return $"⚠️ ПРЕДУПРЕЖДЕНИЕ: Команда '{command}' может быть недоступна в системе.\n\n" +
                   "Конфигурация будет записана, но сервер может не запуститься.\n" +
                   "Убедись что программа установлена и доступна в PATH.";
        }
        
        return null;
    }
    
    /// <summary>
    /// Проверяет доступность команды в системе
    /// </summary>
    private async Task<bool> CheckCommandExistsAsync(string command)
    {
        try
        {
            string shell, shellArgs;
            if (OperatingSystem.IsWindows())
            {
                shell = "cmd.exe";
                shellArgs = $"/c where {command}";
            }
            else
            {
                shell = "/bin/bash";
                shellArgs = $"-c \"which {command}\"";
            }
            
            var startInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = shellArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.WaitForExitAsync();
            
            return process.ExitCode == 0;
        }
        catch
        {
            return true; // В случае ошибки проверки - пропускаем
        }
    }
    
    /// <summary>
    /// Внутренний метод валидации npm пакета
    /// </summary>
    private async Task<(bool IsValid, string? Error)> ValidateNpmPackageInternalAsync(string packageName)
    {
        try
        {
            // Убираем версию из имени пакета для проверки
            var cleanPackageName = packageName;
            
            // Обработка scoped пакетов (@scope/name@version)
            if (packageName.StartsWith("@"))
            {
                var parts = packageName.Split('@');
                if (parts.Length >= 3)
                {
                    // @scope/name@version -> @scope/name
                    cleanPackageName = "@" + parts[1];
                }
            }
            else if (packageName.Contains("@"))
            {
                // name@version -> name
                cleanPackageName = packageName.Split('@')[0];
            }
            
            string shell, shellArgs;
            if (OperatingSystem.IsWindows())
            {
                shell = "cmd.exe";
                shellArgs = $"/c npm view {cleanPackageName} name --json 2>&1";
            }
            else
            {
                shell = "/bin/bash";
                shellArgs = $"-c \"npm view {cleanPackageName} name --json 2>&1\"";
            }
            
            var startInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = shellArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            
            var completed = await Task.Run(() => process.WaitForExit(30000)); // 30 сек таймаут
            
            if (!completed)
            {
                process.Kill();
                return (false, "Таймаут при проверке npm пакета");
            }
            
            if (process.ExitCode != 0)
            {
                return (false, $"Пакет не найден в npm registry. Вывод: {output} {error}");
            }
            
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка проверки: {ex.Message}");
        }
    }
    
    private async Task<McpServersCatalog> LoadCatalogAsync()
    {
        if (_catalog != null)
        {
            return _catalog;
        }
        
        try
        {
            // Загружаем из embedded resource
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "DesktopAssistant.Infrastructure.MCP.Resources.mcp-servers-catalog.json";
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                _catalog = await JsonSerializer.DeserializeAsync<McpServersCatalog>(stream);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading MCP servers catalog");
        }
        
        _catalog ??= new McpServersCatalog();
        return _catalog;
    }
    
    private static int CalculateRelevanceScore(McpCatalogEntry server, string[] queryWords)
    {
        int score = 0;
        var descLower = server.Description.ToLowerInvariant();
        var nameLower = server.Name.ToLowerInvariant();
        var tagsLower = server.Tags.Select(t => t.ToLowerInvariant()).ToList();
        
        foreach (var word in queryWords)
        {
            if (nameLower.Contains(word)) score += 10;
            if (descLower.Contains(word)) score += 5;
            if (tagsLower.Any(t => t.Contains(word))) score += 8;
            if (tagsLower.Contains(word)) score += 3; // Точное совпадение тега
        }
        
        return score;
    }
    
    private static string ConvertToRawReadmeUrl(string githubUrl)
    {
        // https://github.com/owner/repo -> https://raw.githubusercontent.com/owner/repo/main/README.md
        var match = Regex.Match(githubUrl, @"github\.com/([^/]+)/([^/]+)");
        if (match.Success)
        {
            var owner = match.Groups[1].Value;
            var repo = match.Groups[2].Value.TrimEnd('/');
            return $"https://raw.githubusercontent.com/{owner}/{repo}/main/README.md";
        }
        
        return githubUrl;
    }
}
