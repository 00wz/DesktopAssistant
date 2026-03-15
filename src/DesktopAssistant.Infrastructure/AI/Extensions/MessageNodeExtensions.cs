using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Infrastructure.AI.Serialization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Extensions;

/// <summary>
/// Extension methods for MessageNode for working with ChatMessageContent and ChatHistory
/// </summary>
public static class MessageNodeExtensions
{
    /// <summary>
    /// Extracts ChatMessageContent from a node (deserializes Metadata).
    /// </summary>
    /// <exception cref="InvalidOperationException">If Metadata is empty or deserialization failed</exception>
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
    /// Extracts ChatMessageContent from a tool node.
    /// New format: reads SerializedChatMessage from ToolNodeMetadata.
    /// Returns null for pending nodes (ResultJson == null).
    /// </summary>
    /// <exception cref="InvalidOperationException">If ToolNodeMetadata could not be deserialized</exception>
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
    /// Builds ChatHistory from conversation nodes.
    /// The system prompt is injected first if non-empty.
    /// System nodes from the tree (empty anchor nodes) are skipped.
    /// </summary>
    /// <exception cref="InvalidOperationException">If a tool node has no result (pending)</exception>
    public static ChatHistory ToChatHistory(this IEnumerable<MessageNode> messages, string? systemPrompt = null)
    {
        var chatHistory = new ChatHistory();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
            chatHistory.AddSystemMessage(systemPrompt);

        foreach (var message in messages)
        {
            if (message.NodeType == MessageNodeType.Root)
            {
                // Empty anchor System nodes are skipped
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
                // Deserialize stored ChatMessageContent and add to history as-is
                var meta = SummarizationMetadata.TryDeserialize(message.Metadata);
                if (meta != null)
                {
                    foreach (var chatMsg in meta.ToChatMessageContents())
                        chatHistory.Add(chatMsg);
                }
            }
            else
            {
                chatHistory.Add(message.GetChatMessageContent());
            }
        }

        return chatHistory;
    }
}
