using DesktopAssistant.Domain.Entities;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Factory for creating a Semantic Kernel instance.
/// </summary>
public interface IKernelFactory
{
    /// <summary>
    /// Creates a Kernel for the specified assistant profile using the given API key.
    /// </summary>
    Kernel Create(AssistantProfile profile, string apiKey);
}
