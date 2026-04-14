using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Summarization;

/// <summary>
/// Abstract base class for LLM-based chat history reducers that compact conversation history
/// by asking an LLM to call <c>submit_history</c> with the compacted result as a structured
/// list of <see cref="HistoryMessageDto"/> objects.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the built-in <c>ChatHistorySummarizationReducer</c>, which replaces a range of messages
/// with a single free-text summary, subclasses of this reducer produce a proper list of
/// <see cref="ChatMessageContent"/> objects, preserving roles, function calls, and function results.
/// This makes them compatible with agents that use <c>FunctionChoiceBehavior.Required</c>, where
/// function call / result pairs must always appear together in the history.
/// </para>
/// <para>
/// Subclasses provide the JSON schema, system prompt, validation, and DTO-to-message mapping
/// specific to their serialization approach. All common orchestration (boundary detection,
/// LLM invocation, result assembly) lives here.
/// </para>
/// </remarks>
public abstract class ChatHistoryCompactionReducerBase : IChatHistoryReducer
{
    /// <summary>
    /// Default user message appended to the LLM request to trigger compaction.
    /// Shared by all subclasses because it describes the compaction task without
    /// referencing a specific schema.
    /// </summary>
    public const string SharedDefaultReduceUserMessage =
        """
        Your task is to compact the conversation history above by reducing its token
        count as much as possible while preserving all information needed to continue
        the task or conversation seamlessly.

        You have full freedom to choose the best compaction strategy for each part of
        the history. Depending on the content, you may:
        - Remove entire messages that are irrelevant or outdated.
        - Truncate or shorten verbose content within a message, keeping only what
          matters (e.g. if an agent read an entire file but only a few lines are
          relevant, keep only those lines in the tool result).
        - Replace a long chain of messages with a shorter, semantically equivalent
          chain that preserves all meaning and intent.
        - Apply different strategies to different parts of the history.

        When done, call submit_history exactly once with the compacted message list.
        Do not include this instruction message in the compacted history.
        Do not write anything else.
        """;

    private readonly IChatCompletionService _service;
    private readonly int _retainedMessageCount;
    private readonly int _thresholdCount;

    /// <summary>
    /// System prompt sent to the LLM for compaction.
    /// Defaults to the subclass-specific <see cref="GetDefaultReduceSystemPrompt"/>.
    /// </summary>
    public string ReduceSystemPrompt { get; init; }

    /// <summary>
    /// User message appended after the history to trigger compaction.
    /// Defaults to <see cref="SharedDefaultReduceUserMessage"/>.
    /// </summary>
    public string ReduceUserMessage { get; init; } = SharedDefaultReduceUserMessage;

    /// <summary>
    /// Whether to rethrow exceptions that occur during reduction. Defaults to <see langword="true"/>.
    /// </summary>
    public bool FailOnError { get; init; } = true;

    /// <param name="service">Chat completion service used for compaction.</param>
    /// <param name="retainedMessageCount">
    /// Number of messages at the tail of the history that are kept verbatim
    /// without LLM involvement. Everything to the left of this boundary is
    /// sent for compaction.
    /// </param>
    /// <param name="thresholdCount">
    /// Minimum number of compactable messages (beyond <paramref name="retainedMessageCount"/>)
    /// required to trigger reduction. Present because <see cref="IChatHistoryReducer"/> bundles
    /// the trigger decision with the reduction itself; in this implementation it offers only a
    /// best-effort guard, since there is no guarantee the LLM will reduce message count.
    /// </param>
    protected ChatHistoryCompactionReducerBase(
        IChatCompletionService service,
        int retainedMessageCount,
        int? thresholdCount)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (retainedMessageCount < 0)
            throw new ArgumentOutOfRangeException(nameof(retainedMessageCount), "Retained message count must be non-negative.");
        if (thresholdCount.HasValue && thresholdCount.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(thresholdCount), "The reduction threshold length must be greater than zero.");

        _service = service;
        _retainedMessageCount = retainedMessageCount;
        _thresholdCount = thresholdCount ?? 0;

        // Set default system prompt from the subclass
        ReduceSystemPrompt = GetDefaultReduceSystemPrompt();
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ChatMessageContent>?> ReduceAsync(
        IReadOnlyList<ChatMessageContent> chatHistory,
        CancellationToken cancellationToken = default)
    {
        var systemMessage = chatHistory.FirstOrDefault(m => m.Role == AuthorRole.System);
        bool hasSystemMessage = systemMessage is not null;

        int firstNonSystemIndex = hasSystemMessage ? 1 : 0;

        int truncationIndex = LocateSafeReductionIndex(
            chatHistory,
            _retainedMessageCount,
            _thresholdCount);

        if (truncationIndex < firstNonSystemIndex)
            return null;

        try
        {
            // Build LLM request history: compaction prompt + messages to compact
            var llmHistory = new ChatHistory();
            llmHistory.AddSystemMessage(ReduceSystemPrompt);
            for (int i = firstNonSystemIndex; i <= truncationIndex; i++)
                llmHistory.Add(chatHistory[i]);

            llmHistory.AddUserMessage(ReduceUserMessage);

            // Register the submit_history tool and call the LLM
            var submitHistoryFn = GetSubmitHistoryFunction();
            var kernel = new Microsoft.SemanticKernel.Kernel();
            kernel.Plugins.AddFromFunctions("_compaction", [submitHistoryFn]);

            var settings = new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Required(
                    functions: [submitHistoryFn],
                    autoInvoke: false)
            };

            var response = await _service.GetChatMessageContentAsync(
                llmHistory, settings, kernel, cancellationToken).ConfigureAwait(false);

            // Extract the function call
            var callContent = response.Items.OfType<FunctionCallContent>().FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "Reduction failed: LLM did not call submit_history.");

            // Resolve the raw JSON — may arrive as JsonElement, string, or other object
            var rawArg = callContent.Arguments?["messages"];
            var json = rawArg switch
            {
                JsonElement { ValueKind: JsonValueKind.String } je => je.GetString()!,
                JsonElement je => je.GetRawText(),
                string s => s,
                null => throw new InvalidOperationException(
                    "Reduction failed: submit_history was called without a 'messages' argument."),
                var other => JsonSerializer.Serialize(other)
            };

            var dtos = JsonSerializer.Deserialize<List<HistoryMessageDto>>(json)
                ?? throw new InvalidOperationException(
                    "Reduction failed: deserialization of 'messages' returned null.");

            ValidateDtos(dtos);

            var compacted = MapToMessages(dtos);

            return AssembleResult(systemMessage, compacted, chatHistory, truncationIndex);
        }
        catch
        {
            if (FailOnError) throw;
            return null;
        }
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is ChatHistoryCompactionReducerBase other &&
               GetType() == other.GetType() &&
               _thresholdCount == other._thresholdCount &&
               _retainedMessageCount == other._retainedMessageCount &&
               string.Equals(ReduceSystemPrompt, other.ReduceSystemPrompt, StringComparison.Ordinal) &&
               string.Equals(ReduceUserMessage, other.ReduceUserMessage, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(GetType().Name, _thresholdCount, _retainedMessageCount, ReduceSystemPrompt, ReduceUserMessage);

    // -------------------------------------------------------------------------
    // Abstract members — implemented by each strategy
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the default system prompt for this reducer strategy.
    /// Called once during construction to initialize <see cref="ReduceSystemPrompt"/>.
    /// </summary>
    protected abstract string GetDefaultReduceSystemPrompt();

    /// <summary>
    /// Returns the <see cref="KernelFunction"/> representing <c>submit_history</c>
    /// with the JSON schema specific to this strategy.
    /// </summary>
    protected abstract KernelFunction GetSubmitHistoryFunction();

    /// <summary>
    /// Validates structural integrity of the deserialized DTOs.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when a structural rule is violated.</exception>
    protected abstract void ValidateDtos(List<HistoryMessageDto> messages);

    /// <summary>
    /// Maps deserialized DTOs back to <see cref="ChatMessageContent"/> objects.
    /// </summary>
    protected abstract List<ChatMessageContent> MapToMessages(List<HistoryMessageDto> dtos);

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static IEnumerable<ChatMessageContent> AssembleResult(
        ChatMessageContent? systemMessage,
        List<ChatMessageContent> compacted,
        IReadOnlyList<ChatMessageContent> original,
        int truncationIndex)
    {
        if (systemMessage is not null)
            yield return systemMessage;

        foreach (var msg in compacted)
            yield return msg;

        for (int i = truncationIndex + 1; i < original.Count; i++)
            yield return original[i];
    }

    /// <summary>
    /// Returns the index of the last message to include in the compaction range: the rightmost
    /// <see cref="AuthorRole.Assistant"/> message without <see cref="FunctionCallContent"/> that
    /// fits before the retained tail. Returns -1 if no such position exists or the threshold is
    /// not met. Ending on an assistant message keeps function_call/function_result pairs intact
    /// and satisfies strict models (Gemini, Mistral) that disallow a <c>tool → user</c> transition.
    /// </summary>
    private static int LocateSafeReductionIndex(
        IReadOnlyList<ChatMessageContent> chatHistory,
        int retainedMessageCount,
        int thresholdCount)
    {
        if (chatHistory.Count - retainedMessageCount < thresholdCount)
            return -1;

        int messageIndex = chatHistory.Count - retainedMessageCount - 1;

        while (messageIndex >= 0)
        {
            var msg = chatHistory[messageIndex];
            if (msg.Role == AuthorRole.Assistant &&
                !msg.Items.Any(i => i is FunctionCallContent))
                return messageIndex;

            --messageIndex;
        }

        return -1;
    }
}
