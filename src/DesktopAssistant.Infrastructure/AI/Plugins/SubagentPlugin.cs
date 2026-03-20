using System.Text;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Infrastructure.AI.Executors;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace DesktopAssistant.Infrastructure.AI.Plugins;

/// <summary>
/// Plugin registered when a conversation has <c>CanSpawnSubagents = true</c>.
/// Provides tools for creating and interacting with recursive sub-agent conversations.
/// </summary>
public sealed class SubagentPlugin(ISubagentService subagentService, ILogger<SubagentPlugin> logger)
{
    /// <summary>Plugin name used when registering to the kernel.</summary>
    public const string PluginName = "Subagent";

    /// <summary>
    /// Creates a new sub-agent with the given task and waits for it to complete.
    /// Returns the sub-agent's final message (from its complete_task call).
    /// </summary>
    [KernelFunction("create_subagent")]
    [Description(
        "Creates a new sub-agent conversation and sends it the first message as a task. " +
        "Blocks until the sub-agent finishes and returns its result. " +
        "The sub-agent has access to the same tools as the calling agent. " +
        "If interrupted and retried, automatically resumes from where it left off.")]
    public async Task<string> CreateSubagentAsync(
        [Description("The task or first message to send to the sub-agent.")] string first_message,
        [Description("Profile ID to use for this sub-agent. Use list_profiles to see available profiles.")] string profile_id,
        [Description("Optional system prompt that defines the sub-agent's role and constraints.")] string? system_prompt = null,
        [Description("Whether this sub-agent can in turn spawn its own sub-agents. Default: false.")] bool can_spawn_subagents = false,
        [Description("Optional title for the sub-agent conversation. Defaults to the first 60 characters of the task.")] string? name = null,
        ToolExecutionContext executionContext = null!)
    {
        if (!Guid.TryParse(profile_id, out var resolvedProfileId))
            return "Error: Invalid profile_id format.";

        logger.LogInformation(
            "create_subagent called. Parent={ParentId}, ToolNode={ToolNodeId}, CanSpawnSubagents={CanSpawn}, ProfileId={ProfileId}",
            executionContext.ConversationId, executionContext.ToolNodeId, can_spawn_subagents, resolvedProfileId);

        var result = await subagentService.RunSubagentAsync(
            executionContext.ConversationId,
            executionContext.ToolNodeId,
            first_message,
            system_prompt,
            can_spawn_subagents,
            name,
            resolvedProfileId,
            CancellationToken.None);

        return result;
    }

    /// <summary>
    /// Sends a follow-up message to an existing sub-agent and waits for it to complete.
    /// Use this to provide additional instructions or respond to a question from the sub-agent.
    /// </summary>
    [KernelFunction("send_message_to_subagent")]
    [Description(
        "Sends a new message to an existing sub-agent conversation and waits for its response. " +
        "Use this to provide follow-up instructions or answer a question the sub-agent asked.")]
    public async Task<string> SendMessageToSubagentAsync(
        [Description("The conversation ID of the sub-agent (from create_subagent result or list_subagents).")] string conversation_id,
        [Description("The message or follow-up instructions to send.")] string message,
        ToolExecutionContext executionContext = null!)
    {
        if (!Guid.TryParse(conversation_id, out var convId))
            return "Error: Invalid conversation ID format.";

        logger.LogInformation(
            "send_message_to_subagent called. SubagentId={SubagentId}, Parent={ParentId}, ToolNode={ToolNodeId}",
            convId, executionContext.ConversationId, executionContext.ToolNodeId);

        return await subagentService.SendMessageToSubagentAsync(
            convId, executionContext.ToolNodeId, message, CancellationToken.None);
    }

    /// <summary>
    /// Returns a list of all sub-agent conversations created in the current conversation.
    /// </summary>
    [KernelFunction("list_subagents")]
    [Description("Returns a list of all sub-agent conversations that were spawned in this conversation.")]
    public async Task<string> ListSubagentsAsync(ToolExecutionContext executionContext = null!)
    {
        var subagents = await subagentService.GetSubagentsAsync(
            executionContext.ConversationId, CancellationToken.None);

        if (subagents.Count == 0)
            return "No sub-agents have been created in this conversation.";

        var sb = new StringBuilder();
        sb.AppendLine($"Sub-agents ({subagents.Count}):");
        foreach (var sa in subagents)
            sb.AppendLine($"- ID: {sa.Id}  Title: {sa.Title}");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Returns all available assistant profiles that can be used when creating sub-agents.
    /// </summary>
    [KernelFunction("list_profiles")]
    [Description("Returns all available assistant profiles (model, description, ID) that can be used when creating a sub-agent via the profile_id parameter of create_subagent.")]
    public async Task<string> ListProfilesAsync(ToolExecutionContext executionContext = null!)
    {
        var profiles = await subagentService.GetAvailableProfilesAsync(CancellationToken.None);

        if (profiles.Count == 0)
            return "No assistant profiles are available.";

        var sb = new StringBuilder();
        sb.AppendLine($"Available profiles ({profiles.Count}):");
        foreach (var p in profiles)
        {
            sb.Append($"- ID: {p.Id}  Model: {p.ModelId}");
            if (!string.IsNullOrWhiteSpace(p.Description))
                sb.Append($"  Description: {p.Description}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
