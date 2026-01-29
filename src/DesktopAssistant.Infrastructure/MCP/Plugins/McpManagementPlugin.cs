using System.ComponentModel;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private McpServersCatalog? _catalog;
    
    public McpManagementPlugin(ILogger<McpManagementPlugin> logger, HttpClient? httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DesktopAssistant", "1.0"));
    }
    
    /// <summary>
    /// Ищет MCP серверы в каталоге по описанию или тегам
    /// </summary>
    [KernelFunction("search_mcp_servers")]
    [Description("Поиск MCP серверов в каталоге по описанию задачи или ключевым словам. Возвращает список подходящих серверов с их описанием и GitHub URL.")]
    public async Task<string> SearchMcpServersAsync(
        [Description("Поисковый запрос: описание задачи или ключевые слова")] string query)
    {
        _logger.LogDebug("Searching MCP servers with query: {Query}", query);
        
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
        
        resultText += "\nДля установки сервера загрузите его README с помощью fetch_mcp_server_readme и следуйте инструкциям.";
        
        return resultText;
    }
    
    /// <summary>
    /// Загружает README.md из репозитория MCP сервера
    /// </summary>
    [KernelFunction("fetch_mcp_server_readme")]
    [Description("Загружает README.md из GitHub репозитория MCP сервера. README содержит инструкции по установке, необходимые API ключи и формат конфигурации.")]
    public async Task<string> FetchMcpServerReadmeAsync(
        [Description("URL GitHub репозитория (например: https://github.com/upstash/context7-mcp)")] string githubUrl)
    {
        _logger.LogDebug("Fetching README from: {Url}", githubUrl);
        
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
                };
                
                foreach (var altUrl in altUrls)
                {
                    response = await _httpClient.GetAsync(altUrl);
                    if (response.IsSuccessStatusCode) break;
                }
            }
            
            if (!response.IsSuccessStatusCode)
            {
                return $"Не удалось загрузить README из {githubUrl}. Код ответа: {response.StatusCode}";
            }
            
            var readme = await response.Content.ReadAsStringAsync();
            
            // Ограничиваем размер для контекста LLM
            if (readme.Length > 15000)
            {
                readme = readme.Substring(0, 15000) + "\n\n[README обрезан из-за размера]";
            }
            
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
    [Description("Возвращает путь к файлу конфигурации MCP серверов (mcp.json)")]
    public string GetMcpConfigPath()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".desktopasst");
        
        return Path.Combine(configDir, "mcp.json");
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
