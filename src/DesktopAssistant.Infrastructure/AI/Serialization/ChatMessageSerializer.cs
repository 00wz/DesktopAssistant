using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Serialization;

/// <summary>
/// Утилита для сериализации и десериализации ChatMessageContent
/// </summary>
public static class ChatMessageSerializer
{
    private static readonly JsonSerializerOptions _options = CreateOptions();

    /// <summary>
    /// Сериализует ChatMessageContent в JSON строку
    /// </summary>
    public static string Serialize(ChatMessageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return JsonSerializer.Serialize(content, _options);
    }

    /// <summary>
    /// Десериализует JSON строку в ChatMessageContent
    /// </summary>
    /// <exception cref="JsonException">Если десериализация не удалась</exception>
    /// <exception cref="ArgumentNullException">Если json равен null</exception>
    public static ChatMessageContent Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<ChatMessageContent>(json, _options)
            ?? throw new JsonException("Failed to deserialize ChatMessageContent: result is null");
    }

    /// <summary>
    /// Пытается десериализовать JSON строку в ChatMessageContent
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
    /// Создаёт ChatMessageContent из текста с указанной ролью
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
