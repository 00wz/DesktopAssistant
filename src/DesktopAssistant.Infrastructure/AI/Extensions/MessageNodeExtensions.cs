using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Infrastructure.AI.Serialization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Extensions;

/// <summary>
/// Extension методы для MessageNode для работы с ChatMessageContent
/// </summary>
public static class MessageNodeExtensions
{
    /// <summary>
    /// Проверяет, содержит ли узел сериализованный ChatMessageContent в Metadata
    /// </summary>
    public static bool HasStructuredContent(this MessageNode node)
    {
        return !string.IsNullOrEmpty(node.Metadata);
    }

    /// <summary>
    /// Сохраняет ChatMessageContent в узле (сериализация в Metadata)
    /// </summary>
    public static void SetChatMessageContent(this MessageNode node, ChatMessageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Сохраняем текстовое представление в Content для UI
        node.UpdateContent(content.Content ?? string.Empty);

        // Сериализуем полное сообщение в Metadata
        var serialized = ChatMessageSerializer.Serialize(content);
        node.SetMetadata(serialized);
    }

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

    /// <summary>
    /// Извлекает ChatMessageContent из узла, или создаёт его из Content если Metadata пустой
    /// (для обратной совместимости со старыми записями)
    /// </summary>
    public static ChatMessageContent GetOrCreateChatMessageContent(this MessageNode node)
    {
        // Если есть сериализованный контент - десериализуем
        if (node.HasStructuredContent())
        {
            try
            {
                return ChatMessageSerializer.Deserialize(node.Metadata!);
            }
            catch
            {
                // Если десериализация не удалась, создаём из текста
            }
        }
        
        //TODO: Логировать ошибку десериализации

        // Создаём ChatMessageContent из текста (fallback для старых данных)
        var role = node.NodeType switch
        {
            Domain.Enums.MessageNodeType.System => AuthorRole.System,
            Domain.Enums.MessageNodeType.User => AuthorRole.User,
            Domain.Enums.MessageNodeType.Assistant => AuthorRole.Assistant,
            Domain.Enums.MessageNodeType.Tool => AuthorRole.Tool,
            _ => AuthorRole.Assistant
        };

        return new ChatMessageContent(role, node.Content);
    }
}
