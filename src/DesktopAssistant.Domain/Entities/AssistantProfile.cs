namespace DesktopAssistant.Domain.Entities;

/// <summary>
/// AI assistant profile with LLM connection settings.
/// The API key is stored separately in a secure store (DPAPI) and is bound to the profile Id.
/// The system prompt is stored in Conversation.SystemPrompt.
/// </summary>
public class AssistantProfile : BaseEntity
{
    public string Description { get; private set; } = string.Empty;

    // LLM settings (OpenAI-compatible API)
    public string BaseUrl { get; private set; } = string.Empty;
    public string ModelId { get; private set; } = string.Empty;
    public double Temperature { get; private set; } = 0.7;
    public int MaxTokens { get; private set; } = 4096;

    // Navigation properties
    public ICollection<Conversation> Conversations { get; private set; } = new List<Conversation>();

    private AssistantProfile() { } // For EF Core

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
