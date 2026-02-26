using DesktopAssistant.Domain.Entities;
using Microsoft.SemanticKernel;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Фабрика для создания Semantic Kernel.
/// </summary>
public interface IKernelFactory
{
    /// <summary>
    /// Создаёт Kernel для указанного профиля ассистента с API-ключом.
    /// </summary>
    Kernel Create(AssistantProfile profile, string apiKey);
}
