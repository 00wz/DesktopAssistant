using DesktopAssistant.Domain.Enums;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Summarization;

/// <summary>
/// Creates an <see cref="IChatHistoryReducer"/> instance for the requested <see cref="SummarizationSchema"/>.
/// </summary>
public interface IChatHistoryReducerFactory
{
    /// <summary>
    /// Returns a reducer that uses the given <paramref name="schema"/> to compact chat history.
    /// </summary>
    IChatHistoryReducer Create(IChatCompletionService chatCompletionService, SummarizationSchema schema);
}
