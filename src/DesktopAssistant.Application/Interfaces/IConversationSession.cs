using DesktopAssistant.Application.Dtos;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Инкапсулирует состояние и логику одного диалога:
/// LLM-тёрны, tool-вызовы, autoapprove и данные диалога.
/// Является фасадом над <see cref="IChatService"/> — потребители (ChatViewModel, субагенты)
/// не зависят от <see cref="IChatService"/> напрямую.
/// </summary>
public interface IConversationSession : IDisposable
{
    /// <summary>Id диалога.</summary>
    Guid ConversationId { get; }

    /// <summary>Текущее состояние диалога.</summary>
    ConversationState State { get; }

    /// <summary>True пока сессия автоматически обрабатывает тёрн (стриминг / auto-approve).</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Событие публикуется для всех изменений состояния и данных.
    /// Обработчик вызывается из произвольного потока — маршалинг в UI-поток на стороне подписчика.
    /// </summary>
    event EventHandler<SessionEvent> EventOccurred;

    // ── Data facade (над IChatService) ────────────────────────────────────────

    /// <summary>Загружает историю активной ветки.</summary>
    Task<IEnumerable<MessageDto>> LoadHistoryAsync(CancellationToken ct = default);

    /// <summary>Возвращает настройки диалога (системный промпт, профиль).</summary>
    Task<ConversationSettingsDto?> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>Сохраняет системный промпт диалога.</summary>
    Task UpdateSystemPromptAsync(string systemPrompt, CancellationToken ct = default);

    /// <summary>Меняет профиль ассистента для диалога.</summary>
    Task ChangeProfileAsync(Guid profileId, CancellationToken ct = default);

    /// <summary>
    /// Суммаризирует контекст начиная с выбранного узла.
    /// Публикует <see cref="SummarizationSessionEvent"/> для каждого события процесса.
    /// </summary>
    Task SummarizeAsync(Guid selectedNodeId, CancellationToken ct = default);

    /// <summary>
    /// Переключается на альтернативную ветку (sibling) и переинициализирует сессию.
    /// Публикует <see cref="InitializeSessionEvent"/> по завершении.
    /// </summary>
    Task SwitchToSiblingAsync(Guid parentNodeId, Guid newChildId, CancellationToken ct = default);

    // ── LLM-операции ─────────────────────────────────────────────────────────

    /// <summary>
    /// Добавляет сообщение пользователя и запускает LLM-тёрн.
    /// <para>
    /// Если <paramref name="parentNodeId"/> не задан — добавляется к текущему листу (<see cref="ConversationSession.CurrentLeafNodeId"/>).
    /// Если <paramref name="parentNodeId"/> отличается от текущего листа — выполняется ветвление:
    /// сессия переинициализируется и публикует <see cref="InitializeSessionEvent"/>.
    /// </para>
    /// </summary>
    Task SendMessageAsync(string message, Guid? parentNodeId = null, CancellationToken ct = default);

    /// <summary>
    /// Возобновляет диалог с текущего листа (например, после прерывания приложения).
    /// Допустимо только в состоянии <see cref="ConversationState.LastMessageIsUser"/>
    /// или <see cref="ConversationState.AllToolCallsCompleted"/>.
    /// </summary>
    Task ResumeAsync(CancellationToken ct = default);

    /// <summary>
    /// Одобряет ожидающий tool-вызов.
    /// После завершения всех tool-вызовов текущего тёрна автоматически запускает следующий LLM-тёрн.
    /// </summary>
    Task ApproveToolAsync(Guid pendingNodeId, CancellationToken ct = default);

    /// <summary>
    /// Отклоняет ожидающий tool-вызов.
    /// После завершения всех tool-вызовов текущего тёрна автоматически запускает следующий LLM-тёрн.
    /// </summary>
    Task DenyToolAsync(Guid pendingNodeId, CancellationToken ct = default);
}
