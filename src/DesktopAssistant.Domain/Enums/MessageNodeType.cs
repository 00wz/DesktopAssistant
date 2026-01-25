namespace DesktopAssistant.Domain.Enums;

/// <summary>
/// Тип узла сообщения в графе диалога
/// </summary>
public enum MessageNodeType
{
    /// <summary>
    /// Системный промпт
    /// </summary>
    System = 0,
    
    /// <summary>
    /// Сообщение пользователя
    /// </summary>
    User = 1,
    
    /// <summary>
    /// Ответ ассистента
    /// </summary>
    Assistant = 2,
    
    /// <summary>
    /// Узел суммаризации - содержит сводку предыдущего контекста.
    /// При сборке контекста для LLM, алгоритм идёт назад по ветке
    /// и останавливается на этом узле, используя его содержимое
    /// как "точку отсчёта" вместо всей предыдущей истории.
    /// </summary>
    Summary = 3
}
