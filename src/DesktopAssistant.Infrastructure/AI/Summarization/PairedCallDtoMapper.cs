using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Summarization;

/// <summary>
/// Bidirectional mapper between <see cref="ChatMessageContent"/> and <see cref="HistoryMessageDto"/>
/// using the paired <c>function_call</c> / <c>function_result</c> schema with explicit
/// <c>id</c> / <c>call_id</c> correlation.
/// </summary>
public class PairedCallDtoMapper
{
    /// <summary>
    /// Converts a <see cref="ChatMessageContent"/> to a <see cref="HistoryMessageDto"/>.
    /// Unknown KernelContent item types are silently skipped.
    /// </summary>
    public HistoryMessageDto ToDto(ChatMessageContent message)
    {
        var items = new List<HistoryContentItemDto>();

        foreach (var item in message.Items)
        {
            switch (item)
            {
                case TextContent tc:
                    items.Add(new HistoryContentItemDto
                    {
                        Type = "text",
                        Text = tc.Text
                    });
                    break;

                case FunctionCallContent fcc:
                    items.Add(new HistoryContentItemDto
                    {
                        Type = "function_call",
                        Id = fcc.Id,
                        PluginName = fcc.PluginName,
                        FunctionName = fcc.FunctionName,
                        Arguments = SerializeArguments(fcc.Arguments)
                    });
                    break;

                case FunctionResultContent frc:
                    items.Add(new HistoryContentItemDto
                    {
                        Type = "function_result",
                        CallId = frc.CallId,
                        PluginName = frc.PluginName,
                        FunctionName = frc.FunctionName,
                        Result = frc.Result is string s ? s
                            : frc.Result is null ? null
                            : JsonSerializer.Serialize(frc.Result)
                    });
                    break;

                // Unknown types are silently skipped
            }
        }

        return new HistoryMessageDto
        {
            Role = RoleToString(message.Role),
            Items = items
        };
    }

    /// <summary>
    /// Restores a <see cref="ChatMessageContent"/> from a <see cref="HistoryMessageDto"/>.
    /// Unknown item types are silently skipped.
    /// </summary>
    public ChatMessageContent FromDto(HistoryMessageDto dto)
    {
        var role = StringToRole(dto.Role);
        var content = new ChatMessageContent(role, content: null);

        foreach (var item in dto.Items)
        {
            switch (item.Type)
            {
                case "text":
                    content.Items.Add(new TextContent(item.Text));
                    break;

                case "function_call":
                    var args = item.Arguments is { Count: > 0 }
                        ? new KernelArguments(
                            item.Arguments.ToDictionary(kv => kv.Key, kv => (object?)kv.Value))
                        : new KernelArguments();
                    content.Items.Add(new FunctionCallContent(
                        item.FunctionName ?? string.Empty,
                        item.PluginName,
                        item.Id,
                        args));
                    break;

                case "function_result":
                    content.Items.Add(new FunctionResultContent(
                        item.FunctionName,
                        item.PluginName,
                        item.CallId,
                        item.Result));
                    break;

                // Unknown types are silently skipped
            }
        }

        return content;
    }

    private static Dictionary<string, string>? SerializeArguments(KernelArguments? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return null;

        return arguments.ToDictionary(
            kv => kv.Key,
            kv => kv.Value is string s ? s
                : kv.Value is null ? string.Empty
                : JsonSerializer.Serialize(kv.Value));
    }

    private static string RoleToString(AuthorRole role)
    {
        if (role == AuthorRole.User) return "user";
        if (role == AuthorRole.Assistant) return "assistant";
        if (role == AuthorRole.Tool) return "tool";
        if (role == AuthorRole.System) return "system";
        return role.Label;
    }

    private static AuthorRole StringToRole(string role) => role switch
    {
        "user" => AuthorRole.User,
        "assistant" => AuthorRole.Assistant,
        "tool" => AuthorRole.Tool,
        "system" => AuthorRole.System,
        _ => new AuthorRole(role)
    };
}
