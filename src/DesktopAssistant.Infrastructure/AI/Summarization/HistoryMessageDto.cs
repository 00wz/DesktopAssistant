using System.Text.Json.Serialization;

namespace DesktopAssistant.Infrastructure.AI.Summarization;

public record HistoryContentItemDto
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("plugin_name")]
    public string? PluginName { get; init; }

    [JsonPropertyName("function_name")]
    public string? FunctionName { get; init; }

    [JsonPropertyName("arguments")]
    public Dictionary<string, string>? Arguments { get; init; }

    [JsonPropertyName("call_id")]
    public string? CallId { get; init; }

    [JsonPropertyName("result")]
    public string? Result { get; init; }
}

public record HistoryMessageDto
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("items")]
    public required List<HistoryContentItemDto> Items { get; init; }
}
