namespace DesktopAssistant.Domain.Enums;

/// <summary>
/// Identifies the serialization strategy used by the LLM during chat history compaction.
/// </summary>
public enum SummarizationSchema
{
    /// <summary>
    /// Function call and its result are represented as a single <c>tool_interaction</c> item.
    /// Simpler schema — the LLM cannot omit the result because both halves are bundled together.
    /// </summary>
    ToolInteraction = 0,

    /// <summary>
    /// Traditional paired <c>function_call</c> / <c>function_result</c> items with id/call_id correlation.
    /// Matches the training format used by most frontier models.
    /// </summary>
    PairedCall = 1,
}
