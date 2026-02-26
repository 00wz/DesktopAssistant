using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Entities;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Infrastructure.AI;

/// <summary>
/// Фабрика для создания Semantic Kernel.
/// Использует единый OpenAI-совместимый коннектор для всех провайдеров
/// (OpenAI, Azure OpenAI, Ollama, LM Studio, Together AI и др.)
/// </summary>
public class KernelFactory : IKernelFactory
{
    /// <inheritdoc />
    public Kernel Create(AssistantProfile profile, string apiKey)
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
