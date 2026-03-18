using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DesktopAssistant.Application.Services;

/// <summary>
/// Service for managing the conversation node tree.
/// Does not depend on LLM infrastructure (Semantic Kernel).
/// </summary>
public class ConversationService
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IMessageNodeRepository _messageNodeRepository;
    private readonly IAssistantProfileRepository _assistantRepository;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(
        IConversationRepository conversationRepository,
        IMessageNodeRepository messageNodeRepository,
        IAssistantProfileRepository assistantRepository,
        ILogger<ConversationService> logger)
    {
        _conversationRepository = conversationRepository;
        _messageNodeRepository = messageNodeRepository;
        _assistantRepository = assistantRepository;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new conversation with an anchor root node.
    /// The system prompt is stored in Conversation.SystemPrompt and injected via BuildChatHistory.
    /// </summary>
    public async Task<Conversation> CreateConversationAsync(
        string title,
        Guid assistantProfileId,
        string systemPrompt = "",
        ConversationMode mode = ConversationMode.Chat,
        CancellationToken cancellationToken = default)
    {
        _ = await _assistantRepository.GetByIdAsync(assistantProfileId, cancellationToken)
            ?? throw new InvalidOperationException($"Assistant profile {assistantProfileId} not found");

        var conversation = new Conversation(title, assistantProfileId, systemPrompt);
        if (mode != ConversationMode.Chat)
            conversation.SetMode(mode);

        // Add anchor root node (empty System node as an entry point into the tree)
        var rootMessage = conversation.AddRootMessage();

        await _conversationRepository.AddAsync(conversation, cancellationToken);

        // Set ActiveLeafNodeId
        conversation.SetActiveLeafNode(rootMessage.Id);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);

        _logger.LogInformation("Created conversation {ConversationId} with assistant {AssistantId}",
            conversation.Id, assistantProfileId);

        return conversation;
    }

    /// <summary>
    /// Adds a node to the conversation tree. Does not depend on SK types.
    /// metadata — pre-serialized string (or null).
    /// </summary>
    public async Task<MessageNode> AddNodeAsync(
        Guid conversationId,
        Guid parentNodeId,
        MessageNodeType nodeType,
        string content,
        string? metadata = null,
        int tokenCount = 0,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        var parentNode = await _messageNodeRepository.GetByIdAsync(parentNodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Parent message {parentNodeId} not found");

        var message = new MessageNode(conversationId, nodeType, content, parentNodeId, tokenCount);
        if (metadata != null) message.SetMetadata(metadata);

        await _messageNodeRepository.AddAsync(message, cancellationToken);

        parentNode.SetActiveChild(message.Id);
        await _messageNodeRepository.UpdateAsync(parentNode, cancellationToken);

        conversation.SetActiveLeafNode(message.Id);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);

        _logger.LogDebug("Added node {NodeId} ({NodeType}) to conversation {ConversationId}",
            message.Id, nodeType, conversationId);

        return message;
    }

    /// <summary>
    /// Returns the full ordered path from the root to the specified node
    /// </summary>
    public async Task<IEnumerable<MessageNode>> GetBranchPathAsync(
        Guid nodeId,
        CancellationToken cancellationToken = default)
    {
        var path = new List<MessageNode>();
        await foreach (var node in _messageNodeRepository.TraverseToRootAsync(nodeId, cancellationToken))
            path.Add(node);
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Builds context for the LLM — walks back along the branch to a Summary Node or root
    /// </summary>
    public async Task<IEnumerable<MessageNode>> BuildContextAsync(
        Guid headNodeId,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<MessageNode>();
        await foreach (var node in _messageNodeRepository.TraverseToRootAsync(headNodeId, cancellationToken))
        {
            messages.Add(node);
            if (node.IsSummaryNode)
                break;
        }
        messages.Reverse();
        return messages;
    }

    /// <summary>
    /// Returns the system prompt for the conversation
    /// </summary>
    public async Task<string> GetSystemPromptAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken);
        return conversation?.SystemPrompt ?? string.Empty;
    }

    /// <summary>
    /// Changes the conversation mode
    /// </summary>
    public async Task UpdateModeAsync(
        Guid conversationId,
        ConversationMode mode,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        conversation.SetMode(mode);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);

        _logger.LogInformation("Changed mode for conversation {ConversationId} to {Mode}", conversationId, mode);
    }

    /// <summary>
    /// Changes the assistant profile for the conversation
    /// </summary>
    public async Task UpdateAssistantProfileAsync(
        Guid conversationId,
        Guid newProfileId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        _ = await _assistantRepository.GetByIdAsync(newProfileId, cancellationToken)
            ?? throw new InvalidOperationException($"Assistant profile {newProfileId} not found");

        conversation.UpdateAssistantProfile(newProfileId);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);

        _logger.LogInformation("Changed profile for conversation {ConversationId} to {ProfileId}",
            conversationId, newProfileId);
    }

    /// <summary>
    /// Updates the system prompt of the conversation
    /// </summary>
    public async Task UpdateSystemPromptAsync(
        Guid conversationId,
        string systemPrompt,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        conversation.UpdateSystemPrompt(systemPrompt);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);

        _logger.LogInformation("Updated system prompt for conversation {ConversationId}", conversationId);
    }

    /// <summary>
    /// Returns the assistant profile for the specified conversation
    /// </summary>
    public async Task<AssistantProfile?> GetAssistantProfileAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken);
        if (conversation == null) return null;
        if (!conversation.AssistantProfileId.HasValue) return null;
        return await _assistantRepository.GetByIdAsync(conversation.AssistantProfileId.Value, cancellationToken);
    }

    /// <summary>
    /// Gets a conversation by ID
    /// </summary>
    public async Task<Conversation?> GetConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
        => await _conversationRepository.GetByIdAsync(conversationId, cancellationToken);

    /// <summary>
    /// Gets all active conversations
    /// </summary>
    public async Task<IEnumerable<Conversation>> GetActiveConversationsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _conversationRepository.GetActiveConversationsAsync(cancellationToken);
    }

    /// <summary>
    /// Soft-deletes a conversation
    /// </summary>
    public async Task DeleteConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        await _conversationRepository.SoftDeleteAsync(conversationId, cancellationToken);
        _logger.LogInformation("Soft-deleted conversation {ConversationId}", conversationId);
    }

    /// <summary>
    /// Injects a summary node between selectedNode and its children.
    /// After the operation: selectedNode → summaryNode → (former children of selectedNode).
    /// If selectedNode is the conversation leaf, ActiveLeafNodeId is updated to summaryNode.
    /// </summary>
    public async Task<MessageNode> InjectSummaryNodeAsync(
        Guid conversationId,
        Guid selectedNodeId,
        string summaryContent,
        string? metadata = null,
        int tokenCount = 0,
        CancellationToken cancellationToken = default)
    {
        var selectedNode = await _messageNodeRepository.GetByIdAsync(selectedNodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Node {selectedNodeId} not found");

        var oldActiveChildId = selectedNode.ActiveChildId;
        var children = (await _messageNodeRepository.GetChildrenAsync(selectedNodeId, cancellationToken)).ToList();
        var isLeaf = children.Count == 0;

        // Create summary node as a child of selectedNode
        var summaryNode = new MessageNode(conversationId, MessageNodeType.Summary, summaryContent, selectedNodeId, tokenCount);
        if (metadata != null) summaryNode.SetMetadata(metadata);
        await _messageNodeRepository.AddAsync(summaryNode, cancellationToken);

        // Re-parent all former children of selectedNode to summaryNode
        foreach (var child in children)
        {
            child.ReparentTo(summaryNode.Id);
            await _messageNodeRepository.UpdateAsync(child, cancellationToken);
        }

        // summaryNode inherits the active child of selectedNode
        if (oldActiveChildId.HasValue)
        {
            summaryNode.SetActiveChild(oldActiveChildId.Value);
            await _messageNodeRepository.UpdateAsync(summaryNode, cancellationToken);
        }

        // selectedNode now points to summaryNode
        selectedNode.SetActiveChild(summaryNode.Id);
        await _messageNodeRepository.UpdateAsync(selectedNode, cancellationToken);

        // If selectedNode was a leaf — summaryNode becomes the new conversation leaf
        if (isLeaf)
        {
            var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
                ?? throw new InvalidOperationException($"Conversation {conversationId} not found");
            conversation.SetActiveLeafNode(summaryNode.Id);
            await _conversationRepository.UpdateAsync(conversation, cancellationToken);
        }

        _logger.LogInformation(
            "Injected summary node {SummaryNodeId} after {SelectedNodeId} in conversation {ConversationId} (wasLeaf={IsLeaf})",
            summaryNode.Id, selectedNodeId, conversationId, isLeaf);

        return summaryNode;
    }

    /// <summary>
    /// Switches to a sibling message
    /// </summary>
    public async Task SwitchToSiblingAsync(
        Guid conversationId,
        Guid parentNodeId,
        Guid newChildId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        var parentNode = await _messageNodeRepository.GetByIdAsync(parentNodeId, cancellationToken)
            ?? throw new InvalidOperationException($"Parent node {parentNodeId} not found");

        var newChild = await _messageNodeRepository.GetByIdAsync(newChildId, cancellationToken)
            ?? throw new InvalidOperationException($"Child node {newChildId} not found");

        parentNode.SetActiveChild(newChildId);
        await _messageNodeRepository.UpdateAsync(parentNode, cancellationToken);

        var leafNode = await FindLeafFromNodeAsync(newChild, cancellationToken);

        conversation.SetActiveLeafNode(leafNode.Id);
        await _conversationRepository.UpdateAsync(conversation, cancellationToken);

        _logger.LogInformation(
            "Switched to sibling {ChildId} in conversation {ConversationId}, new leaf: {LeafId}",
            newChildId, conversationId, leafNode.Id);
    }

    /// <summary>
    /// Finds the leaf by following the ActiveChildId chain
    /// </summary>
    private async Task<MessageNode> FindLeafFromNodeAsync(
        MessageNode startNode,
        CancellationToken cancellationToken)
    {
        var current = startNode;
        while (current.ActiveChildId.HasValue)
        {
            current = await _messageNodeRepository.GetByIdAsync(
                current.ActiveChildId.Value, cancellationToken)
                ?? throw new InvalidOperationException($"Active child not found");
        }
        return current;
    }
}
