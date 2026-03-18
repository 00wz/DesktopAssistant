using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Infrastructure.AI.Plugins;
using DesktopAssistant.Infrastructure.MCP.Plugins;
using DesktopAssistant.Infrastructure.MCP.Search;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SKKernel = global::Microsoft.SemanticKernel.Kernel;

namespace DesktopAssistant.Infrastructure.AI.Kernel;

/// <summary>
/// Creates a fully configured kernel for executing agent turns:
/// LLM connector + filters + all plugins (CoreTools, McpManagement, MCP tools).
/// </summary>
public class AgentKernelFactory(
    IKernelFactory kernelFactory,
    IMcpServerManager mcpServerManager,
    IMcpConfigurationService mcpConfigurationService,
    IMcpCatalogSearchService mcpCatalogSearch,
    ILoggerFactory loggerFactory)
{
    public SKKernel Create(AssistantProfile profile, string apiKey, ConversationMode mode = ConversationMode.Chat)
    {
        var kernel = kernelFactory.Create(profile, apiKey);

        //kernel.FunctionInvocationFilters.Add(
        //    new FunctionLoggingFilter(loggerFactory.CreateLogger<FunctionLoggingFilter>()));

        kernel.ImportPluginFromObject(
            new CoreToolsPlugin(loggerFactory.CreateLogger<CoreToolsPlugin>()), "CoreTools");

        kernel.ImportPluginFromObject(
            new McpManagementPlugin(
                loggerFactory.CreateLogger<McpManagementPlugin>(),
                mcpServerManager,
                mcpConfigurationService,
                mcpCatalogSearch),
            "McpManagement");

        if (mcpServerManager.GetConnectedServers().Count > 0)
        {
            var mcpToolsPlugin = new McpToolsPlugin(
                mcpServerManager,
                loggerFactory.CreateLogger<McpToolsPlugin>());
            mcpToolsPlugin.RegisterToolsToKernel(kernel);
        }

        if (mode == ConversationMode.Agent)
        {
            kernel.ImportPluginFromObject(new AgentOutputPlugin(), AgentOutputPlugin.PluginName);
        }

        return kernel;
    }
}
