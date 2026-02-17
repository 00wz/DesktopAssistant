using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Application.Services;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Domain.Interfaces;
using DesktopAssistant.Infrastructure.AI.Filters;
using DesktopAssistant.Infrastructure.MCP.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace DesktopAssistant.Infrastructure.AI;

/// <summary>
/// Сервис для взаимодействия с LLM через Semantic Kernel
/// </summary>
public class ChatService : IChatService
{
    private readonly IKernelFactory _kernelFactory;
    private readonly LlmOptions _llmOptions;
    private readonly ConversationService _conversationService;
    private readonly IConversationRepository _conversationRepository;
    private readonly IMessageNodeRepository _messageNodeRepository;
    private readonly IAssistantProfileRepository _assistantRepository;
    private readonly IMcpServerManager _mcpServerManager;
    private readonly IMcpConfigurationService _mcpConfigurationService;
    private readonly ILogger<ChatService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public ChatService(
        IKernelFactory kernelFactory,
        IOptions<LlmOptions> llmOptions,
        ConversationService conversationService,
        IConversationRepository conversationRepository,
        IMessageNodeRepository messageNodeRepository,
        IAssistantProfileRepository assistantRepository,
        IMcpServerManager mcpServerManager,
        IMcpConfigurationService mcpConfigurationService,
        ILoggerFactory loggerFactory,
        ILogger<ChatService> logger)
    {
        _kernelFactory = kernelFactory;
        _llmOptions = llmOptions.Value;
        _conversationService = conversationService;
        _conversationRepository = conversationRepository;
        _messageNodeRepository = messageNodeRepository;
        _assistantRepository = assistantRepository;
        _mcpServerManager = mcpServerManager;
        _mcpConfigurationService = mcpConfigurationService;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Conversation> CreateConversationAsync(
        string title,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        // Получаем профиль ассистента по умолчанию или создаём его
        var assistant = await _assistantRepository.GetDefaultAsync(cancellationToken);
        
        if (assistant == null)
        {
            assistant = new AssistantProfile(
                name: "Default Assistant",
                systemPrompt: systemPrompt ?? "You are a helpful AI assistant.",
                baseUrl: _llmOptions.BaseUrl,
                modelId: _llmOptions.Model,
                isDefault: true);
            await _assistantRepository.AddAsync(assistant, cancellationToken);
            _logger.LogInformation("Created default assistant profile");
        }
        else if (!string.IsNullOrEmpty(systemPrompt))
        {
            // Если передан системный промпт, создаём новый профиль
            assistant = new AssistantProfile(
                name: $"Assistant for {title}",
                systemPrompt: systemPrompt,
                baseUrl: _llmOptions.BaseUrl,
                modelId: _llmOptions.Model,
                isDefault: false);
            await _assistantRepository.AddAsync(assistant, cancellationToken);
        }

        var conversation = await _conversationService.CreateConversationAsync(
            title,
            assistant.Id,
            cancellationToken);

        _logger.LogInformation("Created conversation {ConversationId}: {Title}", conversation.Id, title);
        return conversation;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Conversation>> GetConversationsAsync(CancellationToken cancellationToken = default)
    {
        return await _conversationService.GetActiveConversationsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<MessageNode>> GetConversationHistoryAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken);
        if (conversation == null)
        {
            _logger.LogWarning("Conversation {ConversationId} not found", conversationId);
            return Enumerable.Empty<MessageNode>();
        }

        if (!conversation.ActiveLeafNodeId.HasValue)
        {
            _logger.LogWarning("Conversation {ConversationId} has no active leaf node", conversationId);
            return Enumerable.Empty<MessageNode>();
        }

        return await _conversationService.BuildContextAsync(
            conversation.ActiveLeafNodeId.Value, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MessageNode> AddUserMessageAsync(
        Guid conversationId,
        Guid parentNodeId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var userNode = await _conversationService.AddMessageAsync(
            conversationId,
            parentNodeId,
            MessageNodeType.User,
            content,
            cancellationToken: cancellationToken);

        _logger.LogInformation("[USER MESSAGE] {MessageId}:\n{Content}", userNode.Id, content);

        return userNode;
    }

    /// <inheritdoc />
    public async Task<MessageNode> AddUserMessageAsync(
        Guid conversationId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        if (!conversation.ActiveLeafNodeId.HasValue)
        {
            throw new InvalidOperationException($"Conversation {conversationId} has no active leaf node");
        }

        return await AddUserMessageAsync(conversationId, conversation.ActiveLeafNodeId.Value, content, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MessageNode> GetAssistantResponseAsync(
        Guid conversationId,
        Guid lastMessageId,
        Action<string>? onChunkReceived = null,
        CancellationToken cancellationToken = default)
    {
        // Собираем контекст для LLM
        var contextMessages = await _conversationService.BuildContextAsync(lastMessageId, cancellationToken);
        var chatHistory = BuildChatHistory(contextMessages);

        // Получаем ответ от LLM
        string assistantResponse;
        if (onChunkReceived != null)
        {
            assistantResponse = await GetStreamingResponseAsync(chatHistory, onChunkReceived, cancellationToken);
        }
        else
        {
            assistantResponse = await GetResponseAsync(chatHistory, cancellationToken);
        }

        _logger.LogInformation("[ASSISTANT MESSAGE] Response ({Length} chars):\n{Content}",
            assistantResponse.Length, assistantResponse);

        // Добавляем ответ ассистента
        var assistantNode = await _conversationService.AddMessageAsync(
            conversationId,
            lastMessageId,
            MessageNodeType.Assistant,
            assistantResponse,
            cancellationToken: cancellationToken);

        return assistantNode;
    }

    /// <inheritdoc />
    public async Task<MessageNode> GetAssistantResponseAsync(
        Guid conversationId,
        Action<string>? onChunkReceived = null,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        if (!conversation.ActiveLeafNodeId.HasValue)
        {
            throw new InvalidOperationException($"Conversation {conversationId} has no active leaf node");
        }

        return await GetAssistantResponseAsync(conversationId, conversation.ActiveLeafNodeId.Value, onChunkReceived, cancellationToken);
    }

    private ChatHistory BuildChatHistory(IEnumerable<MessageNode> messages)
    {
        var chatHistory = new ChatHistory();

        foreach (var message in messages)
        {
            switch (message.NodeType)
            {
                case MessageNodeType.System:
                    chatHistory.AddSystemMessage(message.Content);
                    break;
                case MessageNodeType.User:
                    chatHistory.AddUserMessage(message.Content);
                    break;
                case MessageNodeType.Assistant:
                    chatHistory.AddAssistantMessage(message.Content);
                    break;
                case MessageNodeType.Summary:
                    // Summary nodes содержат сжатый контекст - передаём как системное сообщение
                    chatHistory.AddSystemMessage($"[Previous conversation summary]: {message.Content}");
                    break;
            }
        }

        return chatHistory;
    }
    
    private void LogChatHistory(ChatHistory chatHistory)
    {
        _logger.LogInformation("[CHAT HISTORY] Sending {Count} messages to LLM:", chatHistory.Count);
        
        foreach (var message in chatHistory)
        {
            var role = message.Role.Label.ToUpperInvariant();
            var content = message.Content ?? "(empty)";
            _logger.LogInformation("[{Role}] {Content}", role, content);
        }
    }

    private async Task<string> GetResponseAsync(ChatHistory chatHistory, CancellationToken cancellationToken)
    {
        var kernel = CreateKernelWithMcpTools();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        // Включаем автоматический function calling для MCP tools
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        try
        {
            var response = await chatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                kernel,
                cancellationToken);
            return response.Content ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting response from LLM");
            throw;
        }
    }

    private async Task<string> GetStreamingResponseAsync(
        ChatHistory chatHistory,
        Action<string> onChunkReceived,
        CancellationToken cancellationToken)
    {
        var kernel = CreateKernelWithMcpTools();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        // Включаем автоматический function calling для MCP tools
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        var fullResponse = new System.Text.StringBuilder();

        try
        {
            await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
                chatHistory,
                executionSettings,
                kernel,
                cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    fullResponse.Append(chunk.Content);
                    onChunkReceived(chunk.Content);
                }
            }

            return fullResponse.ToString();
        }
        catch (Exception ex)
        {
            var accumulatedContent = fullResponse.ToString();
            
            // Логируем накопленный ответ перед ошибкой
            if (!string.IsNullOrEmpty(accumulatedContent))
            {
                _logger.LogWarning("Streaming was interrupted. Accumulated response ({Length} chars):\n{Content}",
                    accumulatedContent.Length, accumulatedContent);
            }
            
            _logger.LogError(ex, "Error getting streaming response from LLM");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SwitchToSiblingAsync(
        Guid conversationId,
        Guid parentNodeId,
        Guid newChildId,
        CancellationToken cancellationToken = default)
    {
        await _conversationService.SwitchToSiblingAsync(
            conversationId,
            parentNodeId,
            newChildId,
            cancellationToken);
        
        _logger.LogDebug("Switched to sibling {NewChildId} in conversation {ConversationId}",
            newChildId, conversationId);
    }

    /// <summary>
    /// Создаёт Kernel с зарегистрированными MCP tools и базовыми инструментами
    /// </summary>
    private Kernel CreateKernelWithMcpTools()
    {
        var kernel = _kernelFactory.Create();
        
        // 0. Регистрируем фильтр для логирования вызовов функций
        var loggingFilter = new FunctionLoggingFilter(
            _loggerFactory.CreateLogger<FunctionLoggingFilter>());
        kernel.FunctionInvocationFilters.Add(loggingFilter);
        _logger.LogDebug("Registered FunctionLoggingFilter");
        
        // 1. Регистрируем базовые инструменты (execute_command, read_file, write_to_file и т.д.)
        var coreToolsPlugin = new CoreToolsPlugin(
            _loggerFactory.CreateLogger<CoreToolsPlugin>());
        kernel.ImportPluginFromObject(coreToolsPlugin, "CoreTools");
        _logger.LogDebug("Registered CoreToolsPlugin");
        
        // 2. Регистрируем инструменты управления MCP (search_mcp_servers, fetch_mcp_server_readme, install_mcp_server)
        var mcpManagementPlugin = new McpManagementPlugin(
            _loggerFactory.CreateLogger<McpManagementPlugin>(),
            _mcpServerManager,
            _mcpConfigurationService);
        kernel.ImportPluginFromObject(mcpManagementPlugin, "McpManagement");
        _logger.LogDebug("Registered McpManagementPlugin");
        
        // 3. Регистрируем MCP tools из подключённых серверов
        var connectedServers = _mcpServerManager.GetConnectedServers();
        if (connectedServers.Count > 0)
        {
            var mcpToolsPlugin = new McpToolsPlugin(
                _mcpServerManager,
                _loggerFactory.CreateLogger<McpToolsPlugin>());
            mcpToolsPlugin.RegisterToolsToKernel(kernel);
            
            _logger.LogDebug("Registered MCP tools from {ServerCount} servers", connectedServers.Count);
        }
        
        return kernel;
    }
}
