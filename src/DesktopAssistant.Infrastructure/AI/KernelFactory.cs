using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Entities;
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
        _defaultOptions = options?.Value ?? new LlmOptions();
    }

    /// <summary>
    /// Создаёт Kernel с настройками по умолчанию из LlmOptions
    /// </summary>
    public Kernel Create()
    {
        return Create(_defaultOptions);
    }

    /// <summary>
    /// Создаёт Kernel с указанными LlmOptions
    /// </summary>
    public Kernel Create(LlmOptions options)
    {
        if (options == null || !options.IsValid())
            throw new ArgumentException("Invalid LlmOptions provided", nameof(options));

        return BuildKernel(options.BaseUrl, options.Model, options.ApiKey);
    }

    /// <summary>
    /// Создаёт Kernel с настройками из AssistantProfile и явно переданным API-ключом.
    /// </summary>
    public static Kernel Create(AssistantProfile profile, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key must not be empty", nameof(apiKey));

        return BuildKernel(profile.BaseUrl, profile.ModelId, apiKey);
    }

    private static Kernel BuildKernel(string baseUrl, string modelId, string apiKey)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };

        var builder = Kernel.CreateBuilder();

        builder.AddOpenAIChatCompletion(
            modelId: modelId,
            apiKey: apiKey,
            orgId: null,
            serviceId: null,
            httpClient: httpClient
        );

        return builder.Build();
    }
}
