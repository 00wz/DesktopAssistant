using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Infrastructure.MCP.Plugins;

/// <summary>
/// Плагин для регистрации MCP tools как Kernel Functions
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
    /// Регистрирует все MCP tools как Kernel Functions
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
        // Санитизируем имя функции: только ASCII буквы, цифры и подчёркивания
        var rawName = $"{tool.ServerId}_{tool.Name}";
        var functionName = SanitizeFunctionName(rawName);
        var description = $"[MCP:{tool.ServerId}] {tool.Description ?? tool.Name}";
        
        // Создаём делегат для вызова tool
        var method = async (Kernel kernel, KernelArguments arguments) =>
        {
            // Преобразуем KernelArguments в JsonElement
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
        
        // Создаём метаданные параметров из InputSchema
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
        
        // Получаем список required параметров
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
    /// Санитизирует имя функции для совместимости с Semantic Kernel
    /// Допустимы только ASCII буквы, цифры и подчёркивания
    /// </summary>
    private static string SanitizeFunctionName(string name)
    {
        // Заменяем дефисы и точки на подчёркивания
        var sanitized = name.Replace('-', '_').Replace('.', '_');
        
        // Удаляем все недопустимые символы
        sanitized = Regex.Replace(sanitized, @"[^a-zA-Z0-9_]", "");
        
        // Если имя начинается с цифры, добавляем префикс
        if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
        {
            sanitized = "fn_" + sanitized;
        }
        
        // Убираем последовательные подчёркивания
        sanitized = Regex.Replace(sanitized, @"_+", "_");
        
        // Убираем подчёркивания в начале и конце
        sanitized = sanitized.Trim('_');
        
        return sanitized;
    }
}
