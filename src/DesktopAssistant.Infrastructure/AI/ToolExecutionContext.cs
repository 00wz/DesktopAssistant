namespace DesktopAssistant.Infrastructure.AI;

/// <summary>
/// Ambient context injected into every tool-call invocation via <see cref="Microsoft.SemanticKernel.KernelArguments"/>.
/// A plugin function may receive it by declaring a parameter named <see cref="ArgumentKey"/>
/// of this type; SK will bind it automatically.
/// </summary>
public sealed class ToolExecutionContext
{
    /// <summary>The well-known key used to store this object in KernelArguments.</summary>
    public const string ArgumentKey = "executionContext";

    /// <summary>Id of the conversation that originated the tool call.</summary>
    public Guid ConversationId { get; init; }

    /// <summary>Id of the pending tool-call message node being executed.</summary>
    public Guid ToolNodeId { get; init; }
}
