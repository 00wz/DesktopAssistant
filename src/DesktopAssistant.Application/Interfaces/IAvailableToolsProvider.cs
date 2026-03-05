namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Описание доступного tool (SK-плагин или MCP-инструмент).
/// </summary>
public record ToolDescriptor(string PluginName, string FunctionName, string? Description);

/// <summary>
/// Предоставляет актуальный список всех инструментов, доступных агенту:
/// статических SK-плагинов и динамически подключённых MCP-tools.
/// Генерирует событие <see cref="ToolsChanged"/> при изменении состава tools.
/// </summary>
public interface IAvailableToolsProvider
{
    /// <summary>
    /// Возвращает текущий список всех доступных tools.
    /// </summary>
    IReadOnlyList<ToolDescriptor> GetAvailableTools();

    /// <summary>
    /// Вызывается при добавлении или удалении tools (подключение/отключение MCP-сервера).
    /// </summary>
    event EventHandler? ToolsChanged;
}
