using System.Text.Json;
using Microsoft.SemanticKernel;
using OpenAI.Chat;
using SKChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;

namespace DesktopAssistant.Infrastructure.AI;

internal static class TokenUsageHelper
{
    /// <summary>
    /// Извлекает количество входящих и исходящих токенов из ChatMessageContent.
    /// Поддерживает как живой объект ChatTokenUsage (от LLM), так и десериализованный JsonElement (из БД).
    /// </summary>
    public static (int InputTokenCount, int OutputTokenCount, int TotalTokenCount) Extract(SKChatMessageContent message)
    {
        if (message.Metadata?.TryGetValue("Usage", out var usage) != true || usage == null)
            return (0, 0, 0);

        if (usage is ChatTokenUsage tokenUsage)
            return (tokenUsage.InputTokenCount, tokenUsage.OutputTokenCount, tokenUsage.TotalTokenCount);

        if (usage is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            var input = TryGetInt(element, "inputTokenCount", "input_tokens") ?? 0;
            var output = TryGetInt(element, "outputTokenCount", "output_tokens") ?? 0;
            var total = TryGetInt(element, "totalTokenCount", "total_tokens") ?? (input + output);
            return (input, output, total);
        }

        return (0, 0, 0);
    }

    private static int? TryGetInt(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop) && prop.TryGetInt32(out var val))
                return val;
        }
        return null;
    }
}
