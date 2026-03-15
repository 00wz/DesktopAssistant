using System.Text.Json;
using DesktopAssistant.Application.Dtos;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Infrastructure.AI;

/// <summary>
/// Unified metadata structure for a tool node — stored in MessageNode.Metadata.
/// Status is the source of truth for the call's state.
/// ResultJson is null only for Pending nodes (structural invariant).
/// </summary>
internal sealed record ToolNodeMetadata(
    string CallId,
    string PluginName,
    string FunctionName,
    string ArgumentsJson,
    ToolNodeStatus Status,
    string? ResultJson = null,
    string? SerializedChatMessage = null,
    Guid AssistantNodeId = default)
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// Serializes to JSON for storage in MessageNode.Metadata.
    /// </summary>
    internal string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Attempts to deserialize a string as ToolNodeMetadata.
    /// Returns null if the string is empty, invalid JSON, or not a ToolNodeMetadata.
    /// A valid ToolNodeMetadata is identified by CallId != null (distinguishes it from legacy ChatMessageContent JSON).
    /// </summary>
    internal static ToolNodeMetadata? TryDeserialize(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var meta = JsonSerializer.Deserialize<ToolNodeMetadata>(json, JsonOptions);
            return meta?.CallId != null ? meta : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Serializes FunctionCallContent arguments to a JSON string.
    /// </summary>
    internal static string SerializeFunctionArgs(FunctionCallContent functionCall)
    {
        if (functionCall.Arguments == null) return "{}";
        try
        {
            return JsonSerializer.Serialize(functionCall.Arguments, JsonOptions);
        }
        catch
        {
            return functionCall.Arguments.ToString() ?? "{}";
        }
    }
}
