namespace DesktopAssistant.Domain.Enums;

/// <summary>
/// Тип узла сообщения в графе диалога
/// </summary>
public enum MessageNodeType
{
    /// <summary>
    /// Системный промпт
    /// </summary>
    Root = 0,
    
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
    Summary = 3,

    /// <summary>
    /// Результат вызова функции (tool result).
    /// Используется для хранения FunctionResultContent в ручном режиме вызова инструментов.
    /// </summary>
    Tool = 4
}
