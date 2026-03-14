using DesktopAssistant.Infrastructure.MCP.Models;

namespace DesktopAssistant.Infrastructure.MCP.Search;

/// <summary>
/// Сервис поиска MCP серверов в каталоге.
/// Позволяет менять стратегию поиска (keyword, vector и т.д.).
/// </summary>
public interface IMcpCatalogSearchService
{
    /// <summary>
    /// Ищет MCP серверы по запросу, возвращает отсортированные по релевантности результаты.
    /// </summary>
    /// <param name="query">Поисковый запрос или ключевые слова</param>
    /// <param name="maxResults">Максимальное количество результатов</param>
    Task<IReadOnlyList<McpCatalogEntry>> SearchAsync(string query, int maxResults = 5);
}
