#pragma warning disable SKEXP0001

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Summarization;

/// <summary>
/// Reduces chat history by sending the messages-to-compact to an LLM that must
/// call <c>submit_history</c> with the compacted result.  Unlike
/// <c>ChatHistorySummarizationReducer</c>, the output is a structured list of
/// full <see cref="ChatMessageContent"/> objects, not a single summary string.
/// </summary>
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
                "enum": ["user", "assistant", "tool", "system"],
                "description": "Author role of the message."
              },
              "items": {
                "type": "array",
                "description": "Content blocks of the message. An assistant message with function_call blocks must be immediately followed by tool messages with matching function_result blocks.",
                "items": {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["type"],
                  "properties": {
                    "type": {
                      "type": "string",
                      "enum": ["text", "function_call", "function_result"],
                      "description": "Content block discriminator."
                    },
                    "text": {
                      "type": "string",
                      "description": "Plain text content. Required when type=text."
                    },
                    "id": {
                      "type": "string",
                      "description": "Unique call identifier. Required when type=function_call. Must be matched by the call_id of a subsequent function_result."
                    },
                    "plugin_name": {
                      "type": "string",
                      "description": "Plugin name. Used with function_call and function_result."
                    },
                    "function_name": {
                      "type": "string",
                      "description": "Function name. Used with function_call and function_result."
                    },
                    "arguments": {
                      "type": "object",
                      "description": "Key-value function arguments. Present when type=function_call.",
                      "additionalProperties": { "type": "string" }
                    },
                    "call_id": {
                      "type": "string",
                      "description": "Matches the id of the corresponding function_call. Required when type=function_result."
                    },
                    "result": {
                      "type": "string",
                      "description": "Serialized result value. Present when type=function_result."
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
        - Every assistant message that contains function_call items
          must be immediately and consecutively followed by one tool
          message per function_call, each containing a function_result
          whose call_id matches the id of the corresponding
          function_call, in the same order.
        - A function_call and its matching function_result must always
          appear together — never one without the other.
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
    /// Hysteresis: reduction is not triggered unless the history exceeds
    /// <paramref name="retainedMessageCount"/> + <paramref name="thresholdCount"/> messages.
    /// Recommended to avoid calling the LLM on every new message.
    /// </param>
    public ChatHistoryStructuredReducer(
        IChatCompletionService service,
        int retainedMessageCount = 0,
        int? thresholdCount = null)
    {
        ArgumentNullException.ThrowIfNull(service);
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
            var compacted = dtos.Select(mapper.FromDto).ToList();

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
        // Rule 1 — every function_call id must have exactly one matching function_result call_id and vice-versa
        // TODO: remove this rule
        var callIds = messages
            .SelectMany(m => m.Items)
            .Where(i => i.Type == "function_call" && i.Id is not null)
            .Select(i => i.Id!)
            .ToHashSet();

        var resultCallIds = messages
            .SelectMany(m => m.Items)
            .Where(i => i.Type == "function_result" && i.CallId is not null)
            .Select(i => i.CallId!)
            .ToHashSet();

        var missingResults = callIds.Except(resultCallIds).ToList();
        if (missingResults.Count > 0)
            throw new InvalidOperationException(
                $"function_call id(s) have no matching function_result: {string.Join(", ", missingResults)}");

        var extraResults = resultCallIds.Except(callIds).ToList();
        if (extraResults.Count > 0)
            throw new InvalidOperationException(
                $"function_result call_id(s) have no matching function_call: {string.Join(", ", extraResults)}");

        // Rule 2 — adjacency and ordering of function_call / function_result pairs
        for (int msgIdx = 0; msgIdx < messages.Count; msgIdx++)
        {
            var msg = messages[msgIdx];
            if (msg.Role != "assistant") continue;

            var fnCallIds = msg.Items
                .Where(i => i.Type == "function_call" && i.Id is not null)
                .Select(i => i.Id!)
                .ToList();

            if (fnCallIds.Count == 0) continue;

            for (int k = 0; k < fnCallIds.Count; k++)
            {
                int toolMsgIdx = msgIdx + 1 + k;

                if (toolMsgIdx >= messages.Count || messages[toolMsgIdx].Role != "tool")
                    throw new InvalidOperationException(
                        $"Message at index {msgIdx}: expected a tool message at index {toolMsgIdx} " +
                        $"for function_call id '{fnCallIds[k]}'.");

                var toolItems = messages[toolMsgIdx].Items
                    .Where(i => i.Type == "function_result")
                    .ToList();

                if (toolItems.Count != 1)
                    throw new InvalidOperationException(
                        $"Message at index {toolMsgIdx}: expected exactly one function_result item, " +
                        $"found {toolItems.Count}.");

                if (toolItems[0].CallId != fnCallIds[k])
                    throw new InvalidOperationException(
                        $"Message at index {toolMsgIdx}: expected function_result.call_id='{fnCallIds[k]}', " +
                        $"got '{toolItems[0].CallId}'.");
            }
        }
    }

    /// <summary>
    /// Returns the index of the last message that should be included in the compaction range,
    /// ensuring that no function_call/function_result pair is split across the compaction
    /// boundary and the retained tail.
    /// </summary>
    /// <param name="chatHistory">The full chat history.</param>
    /// <param name="retainedMessageCount">
    /// Number of messages at the tail to keep verbatim. The initial candidate for the last
    /// compacted message is <c>chatHistory.Count - retainedMessageCount - 1</c>.
    /// Pass 0 to compact everything (subject to pair-safety adjustments).
    /// </param>
    /// <param name="thresholdCount">
    /// Minimum number of compactable messages required before reduction is triggered.
    /// Reduction is skipped when <c>chatHistory.Count - retainedMessageCount &lt; thresholdCount</c>.
    /// </param>
    /// <returns>
    /// The index of the last message to include in the compaction range, or -1 if reduction
    /// should not occur (history too short, or no safe boundary exists).
    /// </returns>
    private static int LocateSafeReductionIndex(
        IReadOnlyList<ChatMessageContent> chatHistory,
        int retainedMessageCount,
        int thresholdCount)
    {
        // Do not trigger reduction if the number of compactable messages is below the threshold.
        if (chatHistory.Count - retainedMessageCount < thresholdCount)
            return -1;

        // Initial candidate: the last message before the retained tail.
        int messageIndex = chatHistory.Count - retainedMessageCount - 1;

        // Walk back past any function_result messages so we don't leave an orphaned result
        // at the end of the compaction range without its corresponding function_call.
        while (messageIndex >= 0)
        {
            if (!chatHistory[messageIndex].Items.Any(i => i is FunctionResultContent))
                break;
            --messageIndex;
        }

        // Guard: entire compactable range was consumed (e.g. all messages are function_result).
        if (messageIndex < 0)
            return -1;

        // If the candidate message contains a function_call, its paired function_result is
        // in the retained tail — step back one more to keep the pair together there.
        if (chatHistory[messageIndex].Items.Any(i => i is FunctionCallContent))
            --messageIndex;

        return messageIndex;
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
