using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Infrastructure.AI.Serialization;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Infrastructure.AI.Extensions;

/// <summary>
/// Extension методы для MessageNode для работы с ChatMessageContent
/// </summary>
public static class MessageNodeExtensions
{
    /// <summary>
    /// Извлекает ChatMessageContent из узла (десериализация Metadata)
    /// </summary>
    /// <exception cref="InvalidOperationException">Если Metadata пустой или десериализация не удалась</exception>
    public static ChatMessageContent GetChatMessageContent(this MessageNode node)
    {
        if (string.IsNullOrEmpty(node.Metadata))
        {
            throw new InvalidOperationException($"MessageNode {node.Id} does not contain serialized ChatMessageContent");
        }

        try
        {
            return ChatMessageSerializer.Deserialize(node.Metadata);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to deserialize ChatMessageContent from MessageNode {node.Id}", ex);
        }
    }
}
