using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Infrastructure.AI.Filters;

/// <summary>
/// Фильтр для логирования вызовов функций (tools) Semantic Kernel
/// Логирует все вызовы включая аргументы и результаты
/// </summary>
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
        
        // Логируем начало вызова с аргументами
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
            // Вызываем следующий фильтр или саму функцию
            await next(context);
            
            var duration = DateTime.UtcNow - startTime;
            
            // Логируем результат
            try
            {
                var resultValue = context.Result?.GetValue<object>();
                var resultStr = resultValue?.ToString() ?? "(null)";
                
                // Ограничиваем размер результата для логирования
                if (resultStr.Length > 2000)
                {
                    resultStr = resultStr.Substring(0, 2000) + "... [truncated]";
                }
                
                _logger.LogInformation(
                    "[TOOL RESULT] {FunctionName} - Duration: {Duration}ms - Result ({Length} chars): {Result}",
                    functionName, duration.TotalMilliseconds, resultStr.Length, resultStr);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TOOL RESULT] {FunctionName} - Error serializing result", functionName);
            }
            
            // Логируем метаданные если есть (например, token usage)
            if (context.Result?.Metadata != null && context.Result.Metadata.Count > 0)
            {
                try
                {
                    var metadataJson = JsonSerializer.Serialize(context.Result.Metadata, JsonOptions);
                    _logger.LogDebug("[TOOL METADATA] {FunctionName}: {Metadata}", functionName, metadataJson);
                }
                catch
                {
                    // Игнорируем ошибки сериализации метаданных
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
            // Ограничиваем размер строковых значений
            if (kvp.Value is string str && str.Length > 500)
            {
                dict[kvp.Key] = str.Substring(0, 500) + "... [truncated]";
            }
            else
            {
                dict[kvp.Key] = kvp.Value;
            }
        }
        
        return JsonSerializer.Serialize(dict, JsonOptions);
    }
}
