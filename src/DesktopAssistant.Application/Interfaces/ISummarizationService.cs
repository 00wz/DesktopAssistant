using DesktopAssistant.Application.Dtos;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Сервис суммаризации контекста диалога.
/// Создаёт summary-узел в дереве сообщений, который обрезает LLM-контекст при следующем запросе.
/// </summary>
public interface ISummarizationService
{
    /// <summary>
    /// Выполняет суммаризацию контекста начиная с указанного узла.
    /// Возвращает поток событий: SummarizationStartedDto → SummarizationCompletedDto.
    /// </summary>
    IAsyncEnumerable<SummarizationEvent> SummarizeAsync(
        Guid conversationId,
        Guid selectedNodeId,
        CancellationToken cancellationToken = default);
}
