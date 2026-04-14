using DesktopAssistant.Infrastructure.AI.Summarization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Summarization.PairedCallSchema;

/// <summary>
/// Compacts chat history using the paired <c>function_call</c> / <c>function_result</c> schema
/// with explicit <c>id</c> / <c>call_id</c> correlation. This format is closer to the native
/// Semantic Kernel / OpenAI message representation.
/// </summary>
/// <remarks>
/// This approach requires the LLM to maintain id/call_id pairing and strict adjacency between
/// assistant messages (with function_call items) and the corresponding tool messages (with
/// function_result items). Normalization steps handle common LLM quirks (multi-result tool
/// messages, id/call_id confusion).
/// </remarks>
public class ChatHistoryCompactionReducer : ChatHistoryCompactionReducerBase
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
    public const string DefaultReduceUserMessage = SharedDefaultReduceUserMessage;

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
                      "description": "Key-value function arguments. Present when type=function_call. Each value may be a string, number, boolean, array, or object — reproduce the value exactly as it appeared in the original function call.",
                      "additionalProperties": {}
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

    /// <param name="service">Chat completion service used for compaction.</param>
    /// <param name="retainedMessageCount">
    /// Number of messages at the tail of the history that are kept verbatim
    /// without LLM involvement. Everything to the left of this boundary is
    /// sent for compaction.
    /// </param>
    /// <param name="thresholdCount">
    /// Minimum number of compactable messages (beyond <paramref name="retainedMessageCount"/>)
    /// required to trigger reduction.
    /// </param>
    public ChatHistoryCompactionReducer(
        IChatCompletionService service,
        int retainedMessageCount = 0,
        int? thresholdCount = null)
        : base(service, retainedMessageCount, thresholdCount)
    {
    }

    /// <inheritdoc/>
    protected override string GetDefaultReduceSystemPrompt() => DefaultReduceSystemPrompt;

    /// <inheritdoc/>
    protected override KernelFunction GetSubmitHistoryFunction() => s_submitHistoryFn;

    /// <inheritdoc/>
    protected override void ValidateDtos(List<HistoryMessageDto> messages)
    {
        NormalizeToolMessages(messages);
        NormalizeFunctionResultIds(messages);
        ValidateStructure(messages);
    }

    /// <inheritdoc/>
    protected override List<ChatMessageContent> MapToMessages(List<HistoryMessageDto> dtos)
    {
        var mapper = new HistoryDtoMapper();
        return dtos.Select(mapper.FromDto).ToList();
    }

    // -------------------------------------------------------------------------
    // Normalization — fix common LLM quirks before validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Splits any tool message that contains more than one <c>function_result</c> item into
    /// individual tool messages (one result per message). This normalises quirky LLM output
    /// (e.g. Anthropic models grouping all results into a single tool message) into the
    /// canonical 1-result-per-message form required by OpenAI-compatible providers and by
    /// the strict validation that follows.
    /// </summary>
    private static void NormalizeToolMessages(List<HistoryMessageDto> messages)
    {
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            if (msg.Role != "tool") continue;

            var resultItems = msg.Items.Where(it => it.Type == "function_result").ToList();
            if (resultItems.Count <= 1) continue;

            // Replace the multi-result message with N single-result messages (preserve order).
            messages.RemoveAt(i);
            for (int k = resultItems.Count - 1; k >= 0; k--)
            {
                messages.Insert(i, new HistoryMessageDto
                {
                    Role = "tool",
                    Items = [resultItems[k]]
                });
            }
        }
    }

    /// <summary>
    /// Some models (e.g. claude-haiku) emit <c>function_result</c> items with <c>id</c> instead of
    /// <c>call_id</c>. This method copies <c>id</c> → <c>call_id</c> when <c>call_id</c> is absent,
    /// making the output compatible with the validation and mapping steps that follow.
    /// </summary>
    private static void NormalizeFunctionResultIds(List<HistoryMessageDto> messages)
    {
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            bool changed = false;
            var newItems = new List<HistoryContentItemDto>(msg.Items.Count);

            foreach (var item in msg.Items)
            {
                if (item.Type == "function_result" && item.CallId is null && item.Id is not null)
                {
                    newItems.Add(item with { CallId = item.Id });
                    changed = true;
                }
                else
                {
                    newItems.Add(item);
                }
            }

            if (changed)
                messages[i] = msg with { Items = newItems };
        }
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates structural integrity of the compacted history: id uniqueness,
    /// 1-to-1 call/result correspondence, and strict adjacency.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when a structural rule is violated.</exception>
    private static void ValidateStructure(List<HistoryMessageDto> messages)
    {
        // Rule 1 — every function_call id must be unique, every function_result call_id must be unique,
        //          and the two sets must be equal (1-to-1 correspondence).
        var callIdList = messages
            .SelectMany(m => m.Items)
            .Where(i => i.Type == "function_call" && i.Id is not null)
            .Select(i => i.Id!)
            .ToList();

        var resultCallIdList = messages
            .SelectMany(m => m.Items)
            .Where(i => i.Type == "function_result" && i.CallId is not null)
            .Select(i => i.CallId!)
            .ToList();

        var duplicateCallIds = callIdList
            .GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateCallIds.Count > 0)
            throw new InvalidOperationException(
                $"Duplicate function_call id(s): {string.Join(", ", duplicateCallIds)}");

        var duplicateResultCallIds = resultCallIdList
            .GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateResultCallIds.Count > 0)
            throw new InvalidOperationException(
                $"Duplicate function_result call_id(s): {string.Join(", ", duplicateResultCallIds)}");

        // No duplicates confirmed — set comparison is now meaningful
        var callIds = callIdList.ToHashSet();
        var resultCallIds = resultCallIdList.ToHashSet();

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
