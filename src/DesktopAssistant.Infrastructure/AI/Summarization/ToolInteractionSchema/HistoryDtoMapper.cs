using DesktopAssistant.Infrastructure.AI.Summarization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Summarization.ToolInteractionSchema;

/// <summary>
/// Maps <see cref="HistoryMessageDto"/> lists back to <see cref="ChatMessageContent"/> objects
/// using the <c>tool_interaction</c> schema. Each <c>tool_interaction</c> item is expanded into
/// a paired <see cref="FunctionCallContent"/> (in the assistant message) and a separate
/// <see cref="FunctionResultContent"/> (in a generated tool message) with auto-generated matching ids.
/// Consecutive assistant messages are merged.
/// </summary>
public class HistoryDtoMapper
{
    /// <summary>
    /// Restores a list of <see cref="ChatMessageContent"/> from a list of <see cref="HistoryMessageDto"/>.
    /// <c>tool_interaction</c> items in assistant messages are expanded into paired
    /// <see cref="FunctionCallContent"/> / <see cref="FunctionResultContent"/> entries with generated ids,
    /// and the corresponding tool messages are inserted immediately after each assistant message.
    /// Consecutive assistant messages not separated by a tool message are merged into one.
    /// </summary>
    public List<ChatMessageContent> FromDtoList(IEnumerable<HistoryMessageDto> dtos)
    {
        var result = new List<ChatMessageContent>();

        foreach (var dto in dtos)
        {
            switch (dto.Role)
            {
                case "user":
                {
                    var msg = new ChatMessageContent(AuthorRole.User, content: null);
                    foreach (var item in dto.Items)
                    {
                        if (item.Type == "text")
                            msg.Items.Add(new TextContent(item.Text));
                    }
                    result.Add(msg);
                    break;
                }

                case "assistant":
                {
                    var assistantMsg = new ChatMessageContent(AuthorRole.Assistant, content: null);
                    var toolInteractions = new List<(string Id, string? PluginName, string? FunctionName, string? Result)>();

                    foreach (var item in dto.Items)
                    {
                        switch (item.Type)
                        {
                            case "text":
                                assistantMsg.Items.Add(new TextContent(item.Text));
                                break;

                            case "tool_interaction":
                                var id = Guid.NewGuid().ToString("N");
                                var args = item.Arguments is { Count: > 0 }
                                    ? new KernelArguments(
                                        item.Arguments.ToDictionary(kv => kv.Key, kv => (object?)kv.Value))
                                    : new KernelArguments();
                                assistantMsg.Items.Add(new FunctionCallContent(
                                    item.FunctionName ?? string.Empty,
                                    item.PluginName,
                                    id,
                                    args));
                                toolInteractions.Add((id, item.PluginName, item.FunctionName, item.Result));
                                break;
                        }
                    }

                    result.Add(assistantMsg);

                    foreach (var (id, pluginName, functionName, resultValue) in toolInteractions)
                    {
                        var toolMsg = new ChatMessageContent(AuthorRole.Tool, content: null);
                        toolMsg.Items.Add(new FunctionResultContent(
                            functionName,
                            pluginName,
                            id,
                            resultValue));
                        result.Add(toolMsg);
                    }

                    break;
                }
            }
        }

        return MergeConsecutiveAssistantMessages(result);
    }

    private static List<ChatMessageContent> MergeConsecutiveAssistantMessages(List<ChatMessageContent> messages)
    {
        for (int i = 0; i < messages.Count - 1; )
        {
            if (messages[i].Role == AuthorRole.Assistant &&
                messages[i + 1].Role == AuthorRole.Assistant)
            {
                foreach (var item in messages[i + 1].Items)
                    messages[i].Items.Add(item);
                messages.RemoveAt(i + 1);
                // re-check same position
            }
            else
            {
                i++;
            }
        }

        return messages;
    }
}
