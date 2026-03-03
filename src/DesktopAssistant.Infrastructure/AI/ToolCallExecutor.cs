using System.Text.Json;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Application.Services;
using DesktopAssistant.Domain.Interfaces;
using DesktopAssistant.Infrastructure.AI.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Infrastructure.AI;

/// <summary>
/// Выполняет или отклоняет ожидающий tool-вызов.
/// Stateless: все данные восстанавливаются из БД по pendingNodeId.
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
    /// Выполняет pending tool-вызов и обновляет узел результатом.
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

        var profile = await _conversationService.GetAssistantProfileAsync(pendingNode.ConversationId, cancellationToken)
            ?? throw new InvalidOperationException($"AssistantProfile not found for conversation {pendingNode.ConversationId}");

        var apiKey = _credentialStore.GetApiKey(profile.Id)
            ?? throw new InvalidOperationException(
                $"API key not found for profile '{profile.Name}' ({profile.Id}). Please set the API key in profile settings.");

        var kernel = _agentKernelFactory.Create(profile, apiKey);

        string resultJson;
        bool isError = false;
        string? errorMsg = null;

        try
        {
            _logger.LogDebug("[TOOL APPROVE] Invoking {PluginName}.{FunctionName}", meta.PluginName, meta.FunctionName);

            KernelArguments? kernelArgs = null;
            if (!string.IsNullOrEmpty(meta.ArgumentsJson) && meta.ArgumentsJson != "{}")
            {
                var argsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    meta.ArgumentsJson, ToolNodeMetadata.JsonOptions);
                if (argsDict != null)
                {
                    kernelArgs = [];
                    foreach (var kv in argsDict)
                        kernelArgs[kv.Key] = kv.Value;
                }
            }

            var result = await kernel.InvokeAsync(meta.PluginName, meta.FunctionName, kernelArgs, cancellationToken);
            resultJson = result.ToString() ?? string.Empty;

            _logger.LogDebug("[TOOL RESULT] {PluginName}.{FunctionName} → {Result}",
                meta.PluginName, meta.FunctionName, resultJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TOOL ERROR] {PluginName}.{FunctionName} failed", meta.PluginName, meta.FunctionName);
            resultJson = ex.Message;
            isError = true;
            errorMsg = ex.Message;
        }

        await UpdateToolNodeWithResultAsync(pendingNode, meta, resultJson, cancellationToken);

        return new ToolCallResult(isError, resultJson, errorMsg);
    }

    /// <summary>
    /// Отклоняет pending tool-вызов, записывая статус "Denied by user".
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

        await UpdateToolNodeWithResultAsync(pendingNode, meta, deniedResult, cancellationToken);

        return new ToolCallResult(false, deniedResult, null);
    }

    private async Task UpdateToolNodeWithResultAsync(
        Domain.Entities.MessageNode node,
        ToolNodeMetadata meta,
        string resultJson,
        CancellationToken cancellationToken)
    {
        var functionCallForResult = new FunctionCallContent(meta.FunctionName, meta.PluginName, meta.CallId);
        var resultContent = new FunctionResultContent(functionCallForResult, resultJson);
        var resultChatMsg = resultContent.ToChatMessage();

        var updatedMeta = meta with
        {
            ResultJson = resultJson,
            SerializedChatMessage = ChatMessageSerializer.Serialize(resultChatMsg)
        };

        node.SetMetadata(updatedMeta.ToJson());
        await _messageNodeRepository.UpdateAsync(node, cancellationToken);
    }

}
