using DesktopAssistant.Domain.Enums;
using Microsoft.SemanticKernel.ChatCompletion;
using ToolInteractionReducer = DesktopAssistant.Infrastructure.AI.Summarization.ToolInteractionSchema.ChatHistoryCompactionReducer;
using PairedCallReducer = DesktopAssistant.Infrastructure.AI.Summarization.PairedCallSchema.ChatHistoryCompactionReducer;

namespace DesktopAssistant.Infrastructure.AI.Summarization;

/// <summary>
/// Default implementation of <see cref="IChatHistoryReducerFactory"/>.
/// Maps each <see cref="SummarizationSchema"/> value to the corresponding
/// <see cref="ChatHistoryCompactionReducerBase"/> subclass.
/// </summary>
public sealed class ChatHistoryReducerFactory : IChatHistoryReducerFactory
{
    /// <inheritdoc/>
    public IChatHistoryReducer Create(IChatCompletionService chatCompletionService, SummarizationSchema schema)
        => schema switch
        {
            SummarizationSchema.ToolInteraction => new ToolInteractionReducer(chatCompletionService),
            SummarizationSchema.PairedCall      => new PairedCallReducer(chatCompletionService),
            _ => throw new ArgumentOutOfRangeException(nameof(schema), schema,
                     $"No IChatHistoryReducer implementation registered for schema '{schema}'.")
        };
}
