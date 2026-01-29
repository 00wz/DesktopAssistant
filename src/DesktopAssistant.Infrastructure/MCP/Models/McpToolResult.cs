namespace DesktopAssistant.Infrastructure.MCP.Models;

/// <summary>
/// Результат вызова MCP инструмента
/// </summary>
public class McpToolResult
{
    /// <summary>
    /// Успешен ли вызов
    /// </summary>
    public bool IsSuccess { get; set; }
    
    /// <summary>
    /// Текстовое содержимое результата
    /// </summary>
    public string? Content { get; set; }
    
    /// <summary>
    /// Сообщение об ошибке (если IsSuccess == false)
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Создаёт успешный результат
    /// </summary>
    public static McpToolResult Success(string content) => new()
    {
        IsSuccess = true,
        Content = content
    };
    
    /// <summary>
    /// Создаёт результат с ошибкой
    /// </summary>
    public static McpToolResult Error(string errorMessage) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage
    };
}
