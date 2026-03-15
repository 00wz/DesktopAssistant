using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Infrastructure.MCP.Plugins;

/// <summary>
/// Plugin that registers MCP tools as Kernel Functions.
/// </summary>
public class McpToolsPlugin
{
    private readonly IMcpServerManager _serverManager;
    private readonly ILogger<McpToolsPlugin> _logger;

    public McpToolsPlugin(IMcpServerManager serverManager, ILogger<McpToolsPlugin> logger)
    {
        _serverManager = serverManager;
        _logger = logger;
    }

    /// <summary>
    /// Registers all MCP tools as Kernel Functions.
    /// </summary>
    public void RegisterToolsToKernel(Kernel kernel)
    {
        var tools = _serverManager.GetAllTools();

        if (tools.Count == 0)
        {
            _logger.LogDebug("No MCP tools to register");
            return;
        }

        var functions = new List<KernelFunction>();

        foreach (var tool in tools)
        {
            try
            {
                var function = CreateKernelFunction(tool);
                functions.Add(function);
                _logger.LogDebug("Registered MCP tool: {ServerId}_{ToolName}", tool.ServerId, tool.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register MCP tool: {ServerId}_{ToolName}",
                    tool.ServerId, tool.Name);
            }
        }

        if (functions.Count > 0)
        {
            kernel.Plugins.AddFromFunctions("MCP", functions);
            _logger.LogInformation("Registered {Count} MCP tools to Kernel", functions.Count);
        }
    }

    private KernelFunction CreateKernelFunction(McpToolInfoDto tool)
    {
        // Sanitize the function name: only ASCII letters, digits, and underscores
        var rawName = $"{tool.ServerId}_{tool.Name}";
        var functionName = SanitizeFunctionName(rawName);
        var description = $"[MCP:{tool.ServerId}] {tool.Description ?? tool.Name}";

        // Create a delegate to invoke the tool
        var method = async (Kernel kernel, KernelArguments arguments) =>
        {
            // Convert KernelArguments to JsonElement
            var argsDict = new Dictionary<string, object?>();
            foreach (var arg in arguments)
            {
                argsDict[arg.Key] = arg.Value;
            }
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(argsDict));

            var result = await _serverManager.InvokeToolAsync(tool.ServerId, tool.Name, jsonElement);

            if (result.IsSuccess)
            {
                return result.Content ?? string.Empty;
            }
            else
            {
                return $"Error: {result.ErrorMessage}";
            }
        };

        // Build parameter metadata from InputSchema
        var parameters = ParseParametersFromSchema(tool.InputSchema);

        return KernelFunctionFactory.CreateFromMethod(
            method,
            functionName: functionName,
            description: description,
            parameters: parameters
        );
    }

    private static IEnumerable<KernelParameterMetadata> ParseParametersFromSchema(JsonElement? schema)
    {
        if (schema == null || schema.Value.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (!schema.Value.TryGetProperty("properties", out var properties))
        {
            yield break;
        }

        // Get the list of required parameters
        var requiredParams = new HashSet<string>();
        if (schema.Value.TryGetProperty("required", out var required) &&
            required.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in required.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    requiredParams.Add(item.GetString()!);
                }
            }
        }

        foreach (var prop in properties.EnumerateObject())
        {
            var paramName = prop.Name;
            var paramDescription = string.Empty;
            Type paramType = typeof(string);

            if (prop.Value.TryGetProperty("description", out var desc))
            {
                paramDescription = desc.GetString() ?? string.Empty;
            }

            if (prop.Value.TryGetProperty("type", out var typeElement))
            {
                var typeStr = typeElement.GetString();
                paramType = typeStr switch
                {
                    "string" => typeof(string),
                    "number" => typeof(double),
                    "integer" => typeof(int),
                    "boolean" => typeof(bool),
                    "array" => typeof(List<object>),
                    "object" => typeof(Dictionary<string, object>),
                    _ => typeof(string)
                };
            }

            yield return new KernelParameterMetadata(paramName)
            {
                Description = paramDescription,
                ParameterType = paramType,
                IsRequired = requiredParams.Contains(paramName)
            };
        }
    }

    /// <summary>
    /// Sanitizes a function name for Semantic Kernel compatibility.
    /// Only ASCII letters, digits, and underscores are allowed.
    /// </summary>
    private static string SanitizeFunctionName(string name)
    {
        // Replace hyphens and dots with underscores
        var sanitized = name.Replace('-', '_').Replace('.', '_');

        // Remove all disallowed characters
        sanitized = Regex.Replace(sanitized, @"[^a-zA-Z0-9_]", "");

        // If the name starts with a digit, add a prefix
        if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
        {
            sanitized = "fn_" + sanitized;
        }

        // Collapse consecutive underscores
        sanitized = Regex.Replace(sanitized, @"_+", "_");

        // Trim leading and trailing underscores
        sanitized = sanitized.Trim('_');

        return sanitized;
    }
}
