namespace DesktopAssistant.Application.Dtos;

/// <summary>
/// Base type for summarization events — analogous to StreamEvent for the LLM stream.
/// </summary>
public abstract record SummarizationEvent;

/// <summary>Summarization has started.</summary>
public sealed record SummarizationStartedDto(
    Guid ParentNodeId) : SummarizationEvent;

/// <summary>Summarization has completed successfully.</summary>
public sealed record SummarizationCompletedDto(
    Guid SummaryNodeId,
    Guid ParentNodeId,
    DateTime CreatedAt,
    string SummaryContent) : SummarizationEvent;
