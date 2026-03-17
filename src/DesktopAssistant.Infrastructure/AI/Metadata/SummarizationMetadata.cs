using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopAssistant.Infrastructure.AI.Serialization;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Infrastructure.AI.Metadata;

/// <summary>
/// Metadata for a summary node. Stores serialized ChatMessageContent received from ChatHistorySummarizationReducer.
/// Stored in MessageNode.Metadata.
/// </summary>
internal sealed record SummarizationMetadata(string[] SerializedMessages)
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal string ToJson() => JsonSerializer.Serialize(this, _options);

    internal static SummarizationMetadata? TryDeserialize(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<SummarizationMetadata>(json, _options); }
        catch (JsonException) { return null; }
    }

    internal IEnumerable<ChatMessageContent> ToChatMessageContents()
    {
        foreach (var serialized in SerializedMessages)
        {
            if (ChatMessageSerializer.TryDeserialize(serialized, out var msg) && msg != null)
                yield return msg;
        }
    }
}
