using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Entities;
using Microsoft.SemanticKernel;
using SKKernel = global::Microsoft.SemanticKernel.Kernel;

namespace DesktopAssistant.Infrastructure.AI.Kernel;

/// <summary>
/// Factory for creating a Semantic Kernel instance.
/// Uses a single OpenAI-compatible connector for all providers
/// (OpenAI, Azure OpenAI, Ollama, LM Studio, Together AI, etc.)
/// </summary>
public class KernelFactory : IKernelFactory
{
    /// <inheritdoc />
    public SKKernel Create(AssistantProfile profile, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key must not be empty", nameof(apiKey));

        return BuildKernel(profile.BaseUrl, profile.ModelId, apiKey);
    }

    private static SKKernel BuildKernel(string baseUrl, string modelId, string apiKey)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };

        var builder = SKKernel.CreateBuilder();

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
