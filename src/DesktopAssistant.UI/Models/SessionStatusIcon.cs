namespace DesktopAssistant.UI.Models;

/// <summary>
/// Иконка статуса активной сессии диалога в боковой панели.
/// </summary>
public enum SessionStatusIcon
{
    /// <summary>Иконка не отображается.</summary>
    None,

    /// <summary>Анимированная иконка загрузки — сессия выполняет LLM-тёрн.</summary>
    Loading,

    /// <summary>Иконка паузы — ожидание ввода пользователя или возобновления.</summary>
    Paused,

    /// <summary>Анимированная иконка вопроса — ожидание одобрения tool-вызова.</summary>
    Question,

    /// <summary>Иконка ошибки — несоответствие идентификаторов tool-вызовов.</summary>
    Error,
}
