using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Infrastructure.AI.Serialization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Extensions;

/// <summary>
/// Extension методы для MessageNode для работы с ChatMessageContent и ChatHistory
/// </summary>
public static class MessageNodeExtensions
{
    /// <summary>
    /// Извлекает ChatMessageContent из узла (десериализация Metadata).
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
    /// Извлекает ChatMessageContent из tool-узла.
    /// Новый формат: берёт SerializedChatMessage из ToolNodeMetadata.
    /// Возвращает null для pending-узлов (ResultJson == null).
    /// </summary>
    /// <exception cref="InvalidOperationException">Если ToolNodeMetadata не удалось десериализовать</exception>
    public static ChatMessageContent? TryGetToolChatMessageContent(this MessageNode node)
    {
        var meta = ToolNodeMetadata.TryDeserialize(node.Metadata)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize ToolNodeMetadata for node {node.Id}");

        if (meta.ResultJson == null) return null;
        if (string.IsNullOrEmpty(meta.SerializedChatMessage)) return null;
        return ChatMessageSerializer.TryDeserialize(meta.SerializedChatMessage, out var cm) ? cm : null;
    }

    /// <summary>
    /// Строит ChatHistory из узлов диалога.
    /// Системный промпт инжектируется первым если не пустой.
    /// System-узлы из дерева (пустые якорные узлы) пропускаются.
    /// </summary>
    /// <exception cref="InvalidOperationException">Если tool-узел не имеет результата (pending)</exception>
    public static ChatHistory ToChatHistory(this IEnumerable<MessageNode> messages, string? systemPrompt = null)
    {
        var chatHistory = new ChatHistory();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
            chatHistory.AddSystemMessage(systemPrompt);

        foreach (var message in messages)
        {
            if (message.NodeType == MessageNodeType.System)
            {
                // Пустые якорные System-узлы пропускаются
                continue;
            }
            else if (message.NodeType == MessageNodeType.Tool)
            {
                var toolChatMsg = message.TryGetToolChatMessageContent()
                    ?? throw new InvalidOperationException(
                        $"Tool node {message.Id} has no result — cannot build chat history with a pending tool call");
                chatHistory.Add(toolChatMsg);
            }
            else if (message.NodeType == MessageNodeType.Summary)
            {
                // Summary-узел инжектируется как системное сообщение — контекст предыдущего диалога
                if (!string.IsNullOrEmpty(message.Content))
                    chatHistory.AddSystemMessage(message.Content);
            }
            else
            {
                chatHistory.Add(message.GetChatMessageContent());
            }
        }

        return chatHistory;
    }
}
