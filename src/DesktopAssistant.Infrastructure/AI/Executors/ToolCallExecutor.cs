using System.Text.Json;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Application.Services;
using DesktopAssistant.Domain.Interfaces;
using DesktopAssistant.Infrastructure.AI.Kernel;
using DesktopAssistant.Infrastructure.AI.Metadata;
using DesktopAssistant.Infrastructure.AI.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;


namespace DesktopAssistant.Infrastructure.AI.Executors;

/// <summary>
/// Executes or rejects a pending tool call.
/// Stateless: all data is restored from the database by pendingNodeId.
/// </summary>
public class ToolCallExecutor(
    IMessageNodeRepository messageNodeRepository,
    ConversationService conversationService,
    ISecureCredentialStore credentialStore,
    AgentKernelFactory agentKernelFactory,
    ILogger<ToolCallExecutor> logger)
{
    private readonly IMessageNodeRepository _messageNodeRepository = messageNodeRepository;
    private readonly ConversationService _conversationService = conversationService;
    private readonly ISecureCredentialStore _credentialStore = credentialStore;
    private readonly AgentKernelFactory _agentKernelFactory = agentKernelFactory;
    private readonly ILogger<ToolCallExecutor> _logger = logger;

    /// <summary>
    /// Executes the pending tool call and updates the node with the result.
    /// </summary>
    public async Task<ToolCallResult> ApproveAsync(
        Guid pendingNodeId,
        CancellationToken cancellationToken = default)
    {
        var pendingNode = await _messageNodeRepository.GetByIdAsync(pendingNodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Tool node {pendingNodeId} not found");

        var meta = ToolNodeMetadata.TryDeserialize(pendingNode.Metadata)
            ?? throw new InvalidOperationException($"Failed to parse tool metadata for node {pendingNodeId}");

        if (meta.ResultJson != null)
            throw new InvalidOperationException($"Node {pendingNodeId} is not pending (already has result)");

        var conversation = await _conversationService.GetConversationAsync(pendingNode.ConversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {pendingNode.ConversationId} not found");

        var profile = await _conversationService.GetAssistantProfileAsync(pendingNode.ConversationId, cancellationToken)
            ?? throw new InvalidOperationException($"AssistantProfile not found for conversation {pendingNode.ConversationId}");

        var apiKey = _credentialStore.GetApiKey(profile.Id)
            ?? throw new InvalidOperationException(
                $"API key not found for profile '{profile.ModelId}' ({profile.Id}). Please set the API key in profile settings.");

        var kernel = _agentKernelFactory.Create(profile, apiKey, conversation.Mode);

        string resultJson;
        var status = ToolNodeStatus.Completed;

        try
        {
            _logger.LogDebug("[TOOL APPROVE] Invoking {PluginName}.{FunctionName}", meta.PluginName, meta.FunctionName);

            KernelArguments kernelArgs = [];

            if (!string.IsNullOrEmpty(meta.ArgumentsJson) && meta.ArgumentsJson != "{}")
            {
                var argsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    meta.ArgumentsJson, ToolNodeMetadata.JsonOptions);
                if (argsDict != null)
                {
                    foreach (var kv in argsDict)
                        kernelArgs[kv.Key] = kv.Value;
                }
            }

            kernelArgs[ToolExecutionContext.ArgumentKey] = new ToolExecutionContext
            {
                ConversationId = pendingNode.ConversationId,
                ToolNodeId = pendingNodeId
            };

            var result = await kernel.InvokeAsync(meta.PluginName, meta.FunctionName, kernelArgs, cancellationToken);
            resultJson = result.ToString() ?? string.Empty;

            _logger.LogDebug("[TOOL RESULT] {PluginName}.{FunctionName} → {Result}",
                meta.PluginName, meta.FunctionName, resultJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TOOL ERROR] {PluginName}.{FunctionName} failed", meta.PluginName, meta.FunctionName);
            resultJson = ex.Message;
            status = ToolNodeStatus.Failed;
        }

        await UpdateToolNodeWithResultAsync(pendingNode, meta, resultJson, status, cancellationToken);

        return new ToolCallResult(resultJson, status, meta.AssistantNodeId);
    }

    /// <summary>
    /// Rejects the pending tool call, recording the status as "Denied by user".
    /// </summary>
    public async Task<ToolCallResult> DenyAsync(
        Guid pendingNodeId,
        CancellationToken cancellationToken = default)
    {
        var pendingNode = await _messageNodeRepository.GetByIdAsync(pendingNodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Tool node {pendingNodeId} not found");

        var meta = ToolNodeMetadata.TryDeserialize(pendingNode.Metadata)
            ?? throw new InvalidOperationException($"Failed to parse tool metadata for node {pendingNodeId}");

        const string deniedResult = "Denied by user";

        _logger.LogInformation("[TOOL DENIED] Node {NodeId}: {PluginName}.{FunctionName}",
            pendingNodeId, meta.PluginName, meta.FunctionName);

        await UpdateToolNodeWithResultAsync(pendingNode, meta, deniedResult, ToolNodeStatus.Denied, cancellationToken);

        return new ToolCallResult(deniedResult, ToolNodeStatus.Denied, meta.AssistantNodeId);
    }

    private async Task UpdateToolNodeWithResultAsync(
        Domain.Entities.MessageNode node,
        ToolNodeMetadata meta,
        string resultJson,
        ToolNodeStatus status,
        CancellationToken cancellationToken)
    {
        var functionCallForResult = new FunctionCallContent(meta.FunctionName, meta.PluginName, meta.CallId);
        var resultContent = new FunctionResultContent(functionCallForResult, resultJson);
        var resultChatMsg = resultContent.ToChatMessage();

        var updatedMeta = meta with
        {
            ResultJson = resultJson,
            SerializedChatMessage = ChatMessageSerializer.Serialize(resultChatMsg),
            Status = status
        };

        node.SetMetadata(updatedMeta.ToJson());
        await _messageNodeRepository.UpdateAsync(node, cancellationToken);
    }

}
