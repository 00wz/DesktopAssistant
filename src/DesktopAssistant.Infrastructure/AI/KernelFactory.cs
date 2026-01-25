using DesktopAssistant.Application.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Infrastructure.AI;

/// <summary>
/// Фабрика для создания Semantic Kernel.
/// Использует единый OpenAI-совместимый коннектор для всех провайдеров
/// (OpenAI, Azure OpenAI, Ollama, LM Studio, Together AI и др.)
/// </summary>
public class KernelFactory : IKernelFactory
{
    private readonly LlmOptions _defaultOptions;

    public KernelFactory(IOptions<LlmOptions> options)
    {
        _defaultOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));
        
        if (!_defaultOptions.IsValid())
        {
            throw new InvalidOperationException(
                "LlmOptions are not properly configured. Required: BaseUrl, ApiKey, Model");
        }
    }

    /// <summary>
    /// Создаёт Kernel с настройками по умолчанию
    /// </summary>
    public Kernel Create()
    {
        return Create(_defaultOptions);
    }

    /// <summary>
    /// Создаёт Kernel с указанными настройками
    /// </summary>
    public Kernel Create(LlmOptions options)
    {
        if (options == null || !options.IsValid())
        {
            throw new ArgumentException("Invalid LlmOptions provided", nameof(options));
        }

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };

        var builder = Kernel.CreateBuilder();
        
        builder.AddOpenAIChatCompletion(
            modelId: options.Model,
            apiKey: options.ApiKey,
            orgId: null,
            serviceId: null,
            httpClient: httpClient
        );

        return builder.Build();
    }
}
