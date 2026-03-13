namespace DesktopAssistant.Domain.Entities;

/// <summary>
/// Профиль AI-ассистента с настройками подключения к LLM.
/// API-ключ хранится отдельно в защищённом хранилище (DPAPI) и привязан к Id профиля.
/// Системный промпт хранится в Conversation.SystemPrompt.
/// </summary>
public class AssistantProfile : BaseEntity
{
    public string Description { get; private set; } = string.Empty;

    // LLM настройки (OpenAI-совместимый API)
    public string BaseUrl { get; private set; } = string.Empty;
    public string ModelId { get; private set; } = string.Empty;
    public double Temperature { get; private set; } = 0.7;
    public int MaxTokens { get; private set; } = 4096;

    // Навигационные свойства
    public ICollection<Conversation> Conversations { get; private set; } = new List<Conversation>();

    private AssistantProfile() { } // Для EF Core

    public AssistantProfile(
        string description,
        string baseUrl,
        string modelId,
        double temperature = 0.7,
        int maxTokens = 4096)
    {
        Description = description;
        BaseUrl = baseUrl;
        ModelId = modelId;
        Temperature = temperature;
        MaxTokens = maxTokens;
    }

    public void UpdateDescription(string description)
    {
        Description = description;
        MarkAsUpdated();
    }

    public void UpdateModelSettings(string baseUrl, string modelId, double temperature, int maxTokens)
    {
        BaseUrl = baseUrl;
        ModelId = modelId;
        Temperature = temperature;
        MaxTokens = maxTokens;
        MarkAsUpdated();
    }
}
