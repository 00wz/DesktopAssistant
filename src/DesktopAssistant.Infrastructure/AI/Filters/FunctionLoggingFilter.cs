using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Infrastructure.AI.Filters;

/// <summary>
/// Filter for logging Semantic Kernel function (tool) invocations.
/// Logs all calls including arguments and results.
/// </summary>
[Obsolete("Tool logging has been moved to ToolCallExecutor. This filter is no longer used.")]
public sealed class FunctionLoggingFilter : IFunctionInvocationFilter
{
    private readonly ILogger<FunctionLoggingFilter> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    
    public FunctionLoggingFilter(ILogger<FunctionLoggingFilter> logger)
    {
        _logger = logger;
    }
    
    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context, 
        Func<FunctionInvocationContext, Task> next)
    {
        var functionName = $"{context.Function.PluginName}.{context.Function.Name}";
        
        // Log the start of the call with arguments
        try
        {
            var argsJson = SerializeArguments(context.Arguments);
            _logger.LogInformation(
                "[TOOL CALL] {FunctionName} - Args: {Arguments}",
                functionName, argsJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TOOL CALL] {FunctionName} - Error serializing arguments", functionName);
        }
        
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Invoke the next filter or the function itself
            await next(context);
            
            var duration = DateTime.UtcNow - startTime;
            
            // Log the result
            try
            {
                var resultValue = context.Result?.GetValue<object>();
                var resultStr = resultValue?.ToString() ?? "(null)";
                
                _logger.LogInformation(
                    "[TOOL RESULT] {FunctionName} - Duration: {Duration}ms - Result ({Length} chars):\n{Result}",
                    functionName, duration.TotalMilliseconds, resultStr.Length, resultStr);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TOOL RESULT] {FunctionName} - Error serializing result", functionName);
            }
            
            // Log metadata if present (e.g. token usage)
            if (context.Result?.Metadata != null && context.Result.Metadata.Count > 0)
            {
                try
                {
                    var metadataJson = JsonSerializer.Serialize(context.Result.Metadata, JsonOptions);
                    _logger.LogDebug("[TOOL METADATA] {FunctionName}: {Metadata}", functionName, metadataJson);
                }
                catch
                {
                    // Ignore metadata serialization errors
                }
            }
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, 
                "[TOOL ERROR] {FunctionName} - Duration: {Duration}ms - Error: {Error}",
                functionName, duration.TotalMilliseconds, ex.Message);
            throw;
        }
    }
    
    private static string SerializeArguments(KernelArguments? arguments)
    {
        if (arguments == null || arguments.Count == 0)
        {
            return "{}";
        }
        
        var dict = new Dictionary<string, object?>();
        foreach (var kvp in arguments)
        {
            dict[kvp.Key] = kvp.Value;
        }
        
        return JsonSerializer.Serialize(dict, JsonOptions);
    }
}
