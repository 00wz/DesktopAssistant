using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Application.Services;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Domain.Interfaces;
using DesktopAssistant.Infrastructure.AI.Aggregation;
using DesktopAssistant.Infrastructure.AI.Filters;
using DesktopAssistant.Infrastructure.AI.Extensions;
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
        // Создаем ChatMessageContent из текста пользователя
        var userMessage = new ChatMessageContent(AuthorRole.User, content);

        var userNode = await _conversationService.AddChatMessageAsync(
            conversationId,
            parentNodeId,
            userMessage,
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
        var initialChatHistory = BuildChatHistory(contextMessages);
        var initialMessageCount = initialChatHistory.Count;

        // Получаем ответ от LLM с обработкой function calls
        ChatHistory updatedHistory;
        if (onChunkReceived != null)
        {
            updatedHistory = await GetStreamingResponseAsync(initialChatHistory, onChunkReceived, cancellationToken);
        }
        else
        {
            updatedHistory = await GetResponseAsync(initialChatHistory, cancellationToken);
        }

        // Сохраняем ВСЕ новые сообщения из истории (assistant messages, function calls, function results)
        var newMessages = updatedHistory.Skip(initialMessageCount).ToList();

        if (newMessages.Count == 0)
        {
            throw new InvalidOperationException("No new messages were generated by LLM");
        }

        MessageNode? lastNode = null;
        foreach (var message in newMessages)
        {
            var parentId = lastNode?.Id ?? lastMessageId;
            lastNode = await _conversationService.AddChatMessageAsync(
                conversationId,
                parentId,
                message,
                cancellationToken: cancellationToken);

            _logger.LogDebug("[SAVED MESSAGE] {Role} message {MessageId}, parent: {ParentId}",
                message.Role.Label, lastNode.Id, parentId);
        }

        return lastNode ?? throw new InvalidOperationException("Failed to save any messages to conversation");
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
            // Десериализуем полный ChatMessageContent из Metadata
            var chatMessage = message.GetOrCreateChatMessageContent();

            // Специальная обработка для Summary узлов
            if (message.NodeType == MessageNodeType.Summary)
            {
                chatHistory.AddSystemMessage($"[Previous conversation summary]: {chatMessage.Content}");
            }
            else
            {
                // Добавляем полное сообщение с function calls, items и metadata
                chatHistory.Add(chatMessage);
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

    private async Task<ChatHistory> GetResponseAsync(ChatHistory chatHistory, CancellationToken cancellationToken)
    {
        var kernel = CreateKernelWithMcpTools();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        // Отключаем автоматический вызов - будем вызывать вручную
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false)
        };

        // Цикл обработки function calls
        while (!cancellationToken.IsCancellationRequested)
        {
            ChatMessageContent assistantMessage;
            try
            {
                assistantMessage = await chatService.GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings,
                    kernel,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting response from LLM");
                throw;
            }

            // Добавляем сообщение ассистента в историю (с function calls если есть)
            chatHistory.Add(assistantMessage);

            // Извлекаем function calls
            var functionCalls = FunctionCallContent.GetFunctionCalls(assistantMessage).ToList();

            // Если нет вызовов - завершаем цикл
            if (!functionCalls.Any())
            {
                _logger.LogInformation("[ASSISTANT MESSAGE] Response ({Length} chars):\n{Content}",
                    assistantMessage.Content?.Length ?? 0, assistantMessage.Content);
                break;
            }

            _logger.LogInformation("[FUNCTION CALLS] LLM requested {Count} function calls", functionCalls.Count);

            // Выполняем вызовы вручную
            foreach (var functionCall in functionCalls)
            {
                FunctionResultContent resultContent;
                try
                {
                    _logger.LogDebug("[FUNCTION CALL] Invoking {PluginName}.{FunctionName}",
                        functionCall.PluginName, functionCall.FunctionName);

                    resultContent = await functionCall.InvokeAsync(kernel, cancellationToken);

                    _logger.LogDebug("[FUNCTION RESULT] {PluginName}.{FunctionName} returned: {Result}",
                        functionCall.PluginName, functionCall.FunctionName, resultContent.Result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[FUNCTION ERROR] {PluginName}.{FunctionName} failed",
                        functionCall.PluginName, functionCall.FunctionName);
                    resultContent = new FunctionResultContent(functionCall, ex);
                }

                // Добавляем результат в историю
                chatHistory.Add(resultContent.ToChatMessage());
            }

            // Продолжаем цикл - отправляем обновленную историю обратно в LLM
        }

        return chatHistory;
    }

    private async Task<ChatHistory> GetStreamingResponseAsync(
        ChatHistory chatHistory,
        Action<string> onChunkReceived,
        CancellationToken cancellationToken)
    {
        var kernel = CreateKernelWithMcpTools();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        // Отключаем автоматический вызов - будем вызывать вручную
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false)
        };

        // Цикл обработки function calls
        while (!cancellationToken.IsCancellationRequested)
        {
            var aggregator = new StreamingChatMessageAggregator();

            try
            {
                await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
                    chatHistory,
                    executionSettings,
                    kernel,
                    cancellationToken))
                {
                    aggregator.Append(chunk);

                    // Для UI отправляем текст
                    if (!string.IsNullOrEmpty(chunk.Content))
                    {
                        onChunkReceived(chunk.Content);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting streaming response from LLM");
                throw;
            }

            // Собираем полное сообщение
            var assistantMessage = aggregator.Build();
            chatHistory.Add(assistantMessage);

            // Извлекаем function calls
            var functionCalls = FunctionCallContent.GetFunctionCalls(assistantMessage).ToList();

            // Если нет вызовов - завершаем цикл
            if (!functionCalls.Any())
            {
                _logger.LogInformation("[ASSISTANT MESSAGE] Response ({Length} chars):\n{Content}",
                    assistantMessage.Content?.Length ?? 0, assistantMessage.Content);
                break;
            }

            _logger.LogInformation("[FUNCTION CALLS] LLM requested {Count} function calls", functionCalls.Count);

            // Выполняем вызовы вручную
            foreach (var functionCall in functionCalls)
            {
                FunctionResultContent resultContent;
                try
                {
                    _logger.LogDebug("[FUNCTION CALL] Invoking {PluginName}.{FunctionName}",
                        functionCall.PluginName, functionCall.FunctionName);

                    resultContent = await functionCall.InvokeAsync(kernel, cancellationToken);

                    _logger.LogDebug("[FUNCTION RESULT] {PluginName}.{FunctionName} returned: {Result}",
                        functionCall.PluginName, functionCall.FunctionName, resultContent.Result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[FUNCTION ERROR] {PluginName}.{FunctionName} failed",
                        functionCall.PluginName, functionCall.FunctionName);
                    resultContent = new FunctionResultContent(functionCall, ex);
                }

                // Добавляем результат в историю
                chatHistory.Add(resultContent.ToChatMessage());
            }

            // Продолжаем цикл
        }

        return chatHistory;
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
