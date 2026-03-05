namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Сервис хранения настроек автоматического подтверждения tool-вызовов.
/// Позволяет задавать политику auto-approve отдельно для каждого tool.
/// </summary>
public interface IToolApprovalService
{
    /// <summary>
    /// Возвращает true, если tool с указанными pluginName/functionName настроен
    /// на автоматическое выполнение без запроса подтверждения.
    /// </summary>
    Task<bool> IsAutoApprovedAsync(string pluginName, string functionName);

    /// <summary>
    /// Сохраняет настройку auto-approve для конкретного tool.
    /// </summary>
    Task SetAutoApprovedAsync(string pluginName, string functionName, bool value);
}
