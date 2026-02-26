using System.Text.Json;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Infrastructure.AI;

/// <summary>
/// Единая структура метаданных tool-узла — хранится в MessageNode.Metadata для всех стадий жизненного цикла.
/// Pending:   ResultJson == null, SerializedChatMessage == null.
/// Completed: ResultJson != null, SerializedChatMessage != null (сериализованный ChatMessageContent для контекста LLM).
/// Признак pending определяется исключительно по ResultJson == null.
/// </summary>
internal sealed record ToolNodeMetadata(
    string CallId,
    string PluginName,
    string FunctionName,
    string ArgumentsJson,
    string? ResultJson = null,
    string? SerializedChatMessage = null)
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// Сериализует в JSON для хранения в MessageNode.Metadata.
    /// </summary>
    internal string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Пытается десериализовать строку как ToolNodeMetadata.
    /// Возвращает null если строка пустая, невалидный JSON или не является ToolNodeMetadata.
    /// Признак валидного ToolNodeMetadata: CallId != null (отличает от старого ChatMessageContent JSON).
    /// </summary>
    internal static ToolNodeMetadata? TryDeserialize(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var meta = JsonSerializer.Deserialize<ToolNodeMetadata>(json, JsonOptions);
            return meta?.CallId != null ? meta : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Сериализует аргументы FunctionCallContent в JSON-строку.
    /// </summary>
    internal static string SerializeFunctionArgs(FunctionCallContent functionCall)
    {
        if (functionCall.Arguments == null) return "{}";
        try
        {
            return JsonSerializer.Serialize(functionCall.Arguments, JsonOptions);
        }
        catch
        {
            return functionCall.Arguments.ToString() ?? "{}";
        }
    }
}
