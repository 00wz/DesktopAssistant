using DesktopAssistant.Infrastructure.AI.Executors;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace DesktopAssistant.Infrastructure.AI.Plugins;

/// <summary>
/// Plugin registered exclusively in Agent mode (<see cref="Domain.Enums.ConversationMode.Agent"/>).
/// Provides the single terminal tool the LLM must call to hand control back to the host
/// (user or orchestrating agent). Calling this tool ends the agent loop.
/// </summary>
public sealed class AgentOutputPlugin(ILogger<AgentOutputPlugin> logger)
{
    /// <summary>Plugin name used when registering to the kernel and stored in ToolNodeMetadata.</summary>
    public const string PluginName = "AgentOutput";

    /// <summary>
    /// Signals the end of the agent's work and returns a message to the host.
    /// The message may be a final result, a failure explanation, or a question for the host.
    /// </summary>
    [KernelFunction("complete_task")]
    [Description(
        "Call this tool when you are ready to hand control back to the host. " +
        "Use it to return a final result, report a failure, or ask a clarifying question. " +
        "The agent loop will stop after this call.")]
    public string CompleteTask(
        [Description("Message to the host: final result, failure reason, or question.")] string message,
        ToolExecutionContext executionContext)
    {
        logger.LogInformation(
            "complete_task called. ConversationId={ConversationId}, ToolNodeId={ToolNodeId}",
            executionContext.ConversationId,
            executionContext.ToolNodeId);

        return "Tool executed successfully.";
    }
}
