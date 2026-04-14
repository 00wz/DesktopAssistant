using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopAssistant.Infrastructure.AI.Summarization;

public record HistoryContentItemDto
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }   // "text" | "tool_interaction" | "function_call" | "function_result"

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>
    /// Unique call identifier. Used by the paired-call reducer for <c>function_call</c> items.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("plugin_name")]
    public string? PluginName { get; init; }

    [JsonPropertyName("function_name")]
    public string? FunctionName { get; init; }

    /// <summary>
    /// Function arguments. The schema allows any JSON value type per argument — models sometimes
    /// emit numbers, booleans, arrays, or objects instead of strings. The converter coerces them
    /// all to strings so they round-trip correctly into <see cref="Microsoft.SemanticKernel.KernelArguments"/>.
    /// </summary>
    [JsonPropertyName("arguments")]
    [JsonConverter(typeof(AnyValueToStringDictionaryConverter))]
    public Dictionary<string, string>? Arguments { get; init; }

    /// <summary>
    /// Matches the <see cref="Id"/> of the corresponding <c>function_call</c>.
    /// Used by the paired-call reducer for <c>function_result</c> items.
    /// </summary>
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

/// <summary>
/// Deserializes a JSON object into <c>Dictionary&lt;string, string&gt;</c>, accepting any JSON
/// value type (string, number, boolean, null, array, object) and converting each to its string
/// representation. This is necessary because language models sometimes violate the strict
/// <c>{ "type": "string" }</c> schema constraint for function argument values.
/// </summary>
internal sealed class AnyValueToStringDictionaryConverter : JsonConverter<Dictionary<string, string>?>
{
    public override Dictionary<string, string>? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected JSON object for 'arguments', got {reader.TokenType}.");

        var result = new Dictionary<string, string>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return result;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Expected property name in 'arguments', got {reader.TokenType}.");

            string key = reader.GetString()!;
            reader.Read(); // advance to value token

            result[key] = ReadValueAsString(ref reader);
        }

        return result;
    }

    public override void Write(
        Utf8JsonWriter writer, Dictionary<string, string>? value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        foreach (var (k, v) in value)
            writer.WriteString(k, v);
        writer.WriteEndObject();
    }

    private static string ReadValueAsString(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString()!;

            case JsonTokenType.True:
                return "true";

            case JsonTokenType.False:
                return "false";

            case JsonTokenType.Null:
                return string.Empty;

            case JsonTokenType.Number:
                // Prefer integer representation when possible to preserve original semantics.
                if (reader.TryGetInt64(out long l))
                    return l.ToString(CultureInfo.InvariantCulture);
                return reader.GetDouble().ToString(CultureInfo.InvariantCulture);

            case JsonTokenType.StartObject:
            case JsonTokenType.StartArray:
                // Model emitted a raw JSON value instead of a string — serialize it back to a
                // string so it round-trips correctly into KernelArguments.
                // ParseValue advances the reader past the closing } or ] and returns a
                // self-owning JsonElement (no IDisposable needed).
                return JsonElement.ParseValue(ref reader).GetRawText();

            default:
                throw new JsonException(
                    $"Unexpected token type '{reader.TokenType}' in 'arguments' value.");
        }
    }
}
