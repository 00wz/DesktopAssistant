using Microsoft.SemanticKernel;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Фабрика для создания Semantic Kernel.
/// Использует единый OpenAI-совместимый коннектор для всех провайдеров.
/// </summary>
public interface IKernelFactory
{
    /// <summary>
    /// Создаёт Kernel с настройками по умолчанию
    /// </summary>
    Kernel Create();

    /// <summary>
    /// Создаёт Kernel с указанными настройками
    /// </summary>
    Kernel Create(LlmOptions options);
}

/// <summary>
/// Настройки LLM провайдера.
/// Все провайдеры используют OpenAI-совместимый API.
/// </summary>
public class LlmOptions
{
    /// <summary>
    /// Базовый URL API провайдера.
    /// Примеры:
    /// - OpenAI: https://api.openai.com/v1
    /// - Azure OpenAI: https://{resource}.openai.azure.com/openai/deployments/{deployment}
    /// - Ollama: http://localhost:11434/v1
    /// - LM Studio: http://localhost:1234/v1
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API ключ провайдера
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Идентификатор модели
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Проверяет валидность настроек
    /// </summary>
    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Model);
}
