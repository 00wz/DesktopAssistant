using DesktopAssistant.Infrastructure.AI.Summarization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Summarization.ToolInteractionSchema;

/// <summary>
/// Compacts chat history using the <c>tool_interaction</c> schema — a function call and its result
/// are represented as a single item, eliminating the need for id/call_id correlation.
/// </summary>
/// <remarks>
/// This approach is simpler for the LLM: it cannot forget to emit a matching result because
/// both halves live in the same item. No adjacency or id-matching validation is required.
/// The mapper (<see cref="HistoryDtoMapper.FromDtoList"/>) expands <c>tool_interaction</c>
/// items back into the paired <see cref="FunctionCallContent"/> / <see cref="FunctionResultContent"/>
/// format expected by Semantic Kernel.
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
        - Every tool_interaction item must include both arguments and result.
          Never drop one without the other.
        - tool_interaction items are only allowed in assistant messages.
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

    /// <inheritdoc/>
    protected override List<ChatMessageContent> MapToMessages(List<HistoryMessageDto> dtos)
    {
        var mapper = new HistoryDtoMapper();
        return mapper.FromDtoList(dtos);
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
