using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopAssistant.Infrastructure.AI;

/// <summary>
/// Метаданные summary-узла. Хранится в MessageNode.Metadata.
/// </summary>
internal sealed record SummarizationMetadata(
    int InputTokenCount = 0,
    int OutputTokenCount = 0)
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
}
