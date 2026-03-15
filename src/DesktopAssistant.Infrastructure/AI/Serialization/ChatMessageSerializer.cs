using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Serialization;

/// <summary>
/// Utility for serializing and deserializing ChatMessageContent.
/// </summary>
public static class ChatMessageSerializer
{
    private static readonly JsonSerializerOptions _options = CreateOptions();

    /// <summary>
    /// Serializes a ChatMessageContent to a JSON string.
    /// </summary>
    public static string Serialize(ChatMessageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return JsonSerializer.Serialize(content, _options);
    }

    /// <summary>
    /// Deserializes a JSON string into a ChatMessageContent.
    /// </summary>
    /// <exception cref="JsonException">If deserialization fails.</exception>
    /// <exception cref="ArgumentNullException">If json is null.</exception>
    public static ChatMessageContent Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<ChatMessageContent>(json, _options)
            ?? throw new JsonException("Failed to deserialize ChatMessageContent: result is null");
    }

    /// <summary>
    /// Tries to deserialize a JSON string into a ChatMessageContent.
    /// </summary>
    public static bool TryDeserialize(string? json, out ChatMessageContent? content)
    {
        content = null;
        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        try
        {
            content = JsonSerializer.Deserialize<ChatMessageContent>(json, _options);
            return content != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a ChatMessageContent from text with the specified role.
    /// </summary>
    public static ChatMessageContent CreateFromText(string text, AuthorRole role)
    {
        return new ChatMessageContent(role, text);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };
    }
}
