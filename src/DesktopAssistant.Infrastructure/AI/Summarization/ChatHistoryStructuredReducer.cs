using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Summarization;

/// <summary>
/// Reduces chat history by sending the messages-to-compact to an LLM that must call
/// <c>submit_history</c> with the compacted result as a structured list of
/// <see cref="ChatMessageContent"/> objects.
/// </summary>
/// <remarks>
/// Unlike classic summarizers, this reducer does not guarantee that the result contains fewer
/// messages than the input — the output is whatever the LLM returns. This means the hysteresis
/// provided by <c>thresholdCount</c> may not function as intended: if the LLM does not reduce
/// message count, <see cref="ReduceAsync"/> will trigger on every subsequent call.
/// </remarks>
public class ChatHistoryStructuredReducer : IChatHistoryReducer
{
    /// <summary>
    /// Default system prompt used when calling the LLM for compaction.
    /// </summary>
    public const string DefaultReduceSystemPrompt =
        """
        You are a conversation history compactor.

        Hard constraints — never violate these:
        - Code, configuration, identifiers, file paths, and any other exact-value data
          must be preserved verbatim. Never paraphrase or approximate them.
        - Every function_call must remain paired with its function_result.
          Never include one without the other.
        - Every assistant message that contains function_call items must be immediately
          and consecutively followed by tool messages with the matching function_result
          for each function_call, in the same order.
        - Do not reorder messages.
        - Do not invent information that was not in the original history.
        """;

    /// <summary>
    /// Default user message appended to the LLM request to trigger compaction.
    /// </summary>
    public const string DefaultReduceUserMessage =
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

    private const string MessagesJsonSchema =
        """
        {
          "type": "array",
          "description": "The compacted list of conversation messages.",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "required": ["role", "items"],
            "properties": {
              "role": {
                "type": "string",
                "enum": ["user", "assistant"],
                "description": "Author role of the message. Use 'assistant' for messages that include tool_interaction items."
              },
              "items": {
                "type": "array",
                "description": "Content blocks of the message.",
                "items": {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["type"],
                  "properties": {
                    "type": {
                      "type": "string",
                      "enum": ["text", "tool_interaction"],
                      "description": "Content block discriminator. Use 'tool_interaction' to represent a function call together with its result."
                    },
                    "text": {
                      "type": "string",
                      "description": "Plain text content. Required when type=text."
                    },
                    "plugin_name": {
                      "type": "string",
                      "description": "Plugin name. Present when type=tool_interaction."
                    },
                    "function_name": {
                      "type": "string",
                      "description": "Function name. Present when type=tool_interaction."
                    },
                    "arguments": {
                      "type": "object",
                      "description": "Key-value function arguments. Present when type=tool_interaction.",
                      "additionalProperties": {}
                    },
                    "result": {
                      "type": "string",
                      "description": "Serialized result value. Present when type=tool_interaction."
                    }
                  }
                }
              }
            }
          }
        }
        """;

    private const string SubmitHistoryDescription =
        """
        Submits the compacted conversation history. Call this function
        exactly once with the result of your compaction work.

        Structural rules for the messages argument:
        - Use role 'assistant' for messages that include tool_interaction items.
        - A tool_interaction item represents a single function call together
          with its result. Both arguments and result must always be present.
        - Multiple tool_interaction items in a single assistant message are allowed.
        - Messages must remain in their original chronological order.
        """;

    private static readonly KernelFunction s_submitHistoryFn = CreateSubmitHistoryFn();

    private readonly IChatCompletionService _service;
    private readonly int _retainedMessageCount;
    private readonly int _thresholdCount;

    /// <summary>
    /// System prompt sent to the LLM for compaction. Defaults to <see cref="DefaultReduceSystemPrompt"/>.
    /// </summary>
    public string ReduceSystemPrompt { get; init; } = DefaultReduceSystemPrompt;

    /// <summary>
    /// User message appended after the history to trigger compaction.
    /// Defaults to <see cref="DefaultReduceUserMessage"/>.
    /// </summary>
    public string ReduceUserMessage { get; init; } = DefaultReduceUserMessage;

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
    public ChatHistoryStructuredReducer(
        IChatCompletionService service,
        int retainedMessageCount = 0,
        int? thresholdCount = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (retainedMessageCount < 0)
            throw new ArgumentOutOfRangeException(nameof(retainedMessageCount), "Retained message count must be non-negative.");
        if (thresholdCount.HasValue && thresholdCount.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(thresholdCount), "The reduction threshold length must be greater than zero.");

        _service = service;
        _retainedMessageCount = retainedMessageCount;
        _thresholdCount = thresholdCount ?? 0;
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
            var kernel = new Microsoft.SemanticKernel.Kernel();
            kernel.Plugins.AddFromFunctions("_compaction", [s_submitHistoryFn]);

            var settings = new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Required(
                    functions: [s_submitHistoryFn],
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

            Validate(dtos);

            var mapper = new HistoryMessageDtoMapper();
            var compacted = mapper.FromDtoList(dtos);

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
        return obj is ChatHistoryStructuredReducer other &&
               _thresholdCount == other._thresholdCount &&
               _retainedMessageCount == other._retainedMessageCount &&
               string.Equals(ReduceSystemPrompt, other.ReduceSystemPrompt, StringComparison.Ordinal) &&
               string.Equals(ReduceUserMessage, other.ReduceUserMessage, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(nameof(ChatHistoryStructuredReducer), _thresholdCount, _retainedMessageCount, ReduceSystemPrompt, ReduceUserMessage);

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
    /// Validates structural integrity of the compacted history returned by the LLM.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when a structural rule is violated.</exception>
    private static void Validate(List<HistoryMessageDto> messages)
    {
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (msg.Role == "assistant") continue;

            bool hasToolInteraction = msg.Items.Any(it => it.Type == "tool_interaction");
            if (hasToolInteraction)
                throw new InvalidOperationException(
                    $"Message at index {i} has role '{msg.Role}' but contains tool_interaction items. " +
                    "tool_interaction items are only allowed in assistant messages.");
        }
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

    private static KernelFunction CreateSubmitHistoryFn()
    {
        return KernelFunctionFactory.CreateFromMethod(
            method: static (string messages) => messages,
            functionName: "submit_history",
            description: SubmitHistoryDescription,
            parameters:
            [
                new KernelParameterMetadata("messages")
                {
                    ParameterType = typeof(string),
                    IsRequired = true,
                    Schema = KernelJsonSchema.Parse(MessagesJsonSchema)
                }
            ]);
    }
}
