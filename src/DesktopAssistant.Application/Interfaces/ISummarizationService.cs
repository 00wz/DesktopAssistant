using DesktopAssistant.Application.Dtos;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Service for summarizing a conversation's context.
/// Creates a summary node in the message tree that trims the LLM context on the next request.
/// </summary>
public interface ISummarizationService
{
    /// <summary>
    /// Summarizes the context starting from the specified node.
    /// Returns a stream of events: SummarizationStartedDto → SummarizationCompletedDto.
    /// </summary>
    IAsyncEnumerable<SummarizationEvent> SummarizeAsync(
        Guid conversationId,
        Guid selectedNodeId,
        CancellationToken cancellationToken = default);
}
