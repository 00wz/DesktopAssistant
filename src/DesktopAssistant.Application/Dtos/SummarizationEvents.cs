namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Базовый тип событий суммаризации — аналог StreamEvent для LLM-стрима.
/// </summary>
public abstract record SummarizationEvent;

/// <summary>Суммаризация начата.</summary>
public sealed record SummarizationStartedDto : SummarizationEvent;

/// <summary>Суммаризация успешно завершена.</summary>
public sealed record SummarizationCompletedDto(
    Guid SummaryNodeId,
    string SummaryContent) : SummarizationEvent;
