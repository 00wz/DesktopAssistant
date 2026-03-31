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
[Experimental("SKEXP0001")]
public class ChatHistoryStructuredReducer : IChatHistoryReducer
{
    /// <summary>
    /// Default system prompt used when calling the LLM for compaction.
    /// </summary>
    public const string DefaultReduceSystemPrompt =
        """
        You are a conversation history compactor. Your sole task is to reduce the
        token count of the provided conversation history and then submit the result
        by calling submit_history exactly once.

        You have full freedom to choose the best compaction strategy for each part
        of the history. Depending on the content, you may:
        - Remove entire messages that are irrelevant or outdated.
        - Truncate or shorten verbose text within a message, keeping only the
          essential information (e.g. if an agent read an entire file but only a
          few lines matter, keep only those lines).
        - Replace a long chain of messages with a shorter, semantically equivalent
          chain, as long as all meaning and intent are preserved.
        - Apply different strategies to different parts of the history within a
          single compaction pass.

        Hard constraints that must never be violated:
        - Code, configuration, identifiers, file paths, and any other exact-value
          data must be preserved verbatim — never paraphrase or approximate them.
        - Every function_call must remain paired with its function_result.
          Never include one without the other.
        - Every assistant message that contains function_call items must be
          immediately and consecutively followed by tool messages with the matching
          function_result for each function_call, in the same order.
        - Do not reorder messages.
        - Do not invent information that was not in the original history.
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
        int retainedMessageCount,
        int? thresholdCount = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (retainedMessageCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(retainedMessageCount), "Retained message count must be greater than zero.");
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

        int truncationIndex = LocateSafeReductionIndex(
            chatHistory,
            _retainedMessageCount,
            _thresholdCount,
            hasSystemMessage: hasSystemMessage);

        if (truncationIndex < 0)
            return null;

        int firstNonSystemIndex = hasSystemMessage ? 1 : 0;

        try
        {
            // Build LLM request history: compaction prompt + messages to compact
            var llmHistory = new ChatHistory();
            llmHistory.AddSystemMessage(ReduceSystemPrompt);
            for (int i = firstNonSystemIndex; i < truncationIndex; i++)
                llmHistory.Add(chatHistory[i]);

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
               string.Equals(ReduceSystemPrompt, other.ReduceSystemPrompt, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(nameof(ChatHistoryStructuredReducer), _thresholdCount, _retainedMessageCount, ReduceSystemPrompt);

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

        for (int i = truncationIndex; i < original.Count; i++)
            yield return original[i];
    }

    /// <summary>
    /// Validates structural integrity of the compacted history returned by the LLM.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when a structural rule is violated.</exception>
    private static void Validate(List<HistoryMessageDto> messages)
    {
        // Rule 1 — every function_call id must have exactly one matching function_result call_id and vice-versa
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
    /// Reproduces the logic of the internal <c>ChatHistoryReducerExtensions.LocateSafeReductionIndex</c>
    /// (unavailable from external assemblies).
    /// </summary>
    private static int LocateSafeReductionIndex(
        IReadOnlyList<ChatMessageContent> chatHistory,
        int targetCount,
        int thresholdCount,
        bool hasSystemMessage = false)
    {
        targetCount -= hasSystemMessage ? 1 : 0;

        // Threshold index: history must be longer than this for reduction to trigger
        int thresholdIndex = chatHistory.Count - thresholdCount - targetCount;

        if (thresholdIndex <= 0)
            return -1;

        // Start from the desired truncation target and walk back past function-related content
        int messageIndex = chatHistory.Count - targetCount;

        while (messageIndex >= 0)
        {
            if (!chatHistory[messageIndex].Items.Any(i => i is FunctionCallContent || i is FunctionResultContent))
                break;
            --messageIndex;
        }

        int targetIndex = messageIndex;

        // Prefer a user message as the truncation point for better context cohesion
        while (messageIndex >= thresholdIndex)
        {
            if (chatHistory[messageIndex].Role == AuthorRole.User)
                return messageIndex;
            --messageIndex;
        }

        return targetIndex;
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
