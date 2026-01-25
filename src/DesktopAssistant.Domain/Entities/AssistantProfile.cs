namespace DesktopAssistant.Domain.Entities;

/// <summary>
/// Профиль AI-ассистента с настройками для диалога.
/// </summary>
public class AssistantProfile : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? WakeWord { get; private set; }
    public string SystemPrompt { get; private set; } = string.Empty;
    public string? VoiceName { get; private set; }
    
    // LLM настройки (OpenAI-совместимый API)
    public string BaseUrl { get; private set; } = string.Empty;
    public string ModelId { get; private set; } = string.Empty;
    public double Temperature { get; private set; } = 0.7;
    public int MaxTokens { get; private set; } = 4096;
    
    public bool IsDefault { get; private set; }
    
    // Навигационные свойства
    public ICollection<Conversation> Conversations { get; private set; } = new List<Conversation>();

    private AssistantProfile() { } // Для EF Core

    public AssistantProfile(
        string name,
        string systemPrompt,
        string baseUrl,
        string modelId,
        string? wakeWord = null,
        string? voiceName = null,
        double temperature = 0.7,
        int maxTokens = 4096,
        bool isDefault = false)
    {
        Name = name;
        SystemPrompt = systemPrompt;
        BaseUrl = baseUrl;
        ModelId = modelId;
        WakeWord = wakeWord ?? name.ToLowerInvariant();
        VoiceName = voiceName;
        Temperature = temperature;
        MaxTokens = maxTokens;
        IsDefault = isDefault;
    }

    public void UpdateName(string name)
    {
        Name = name;
        MarkAsUpdated();
    }

    public void UpdateWakeWord(string wakeWord)
    {
        WakeWord = wakeWord;
        MarkAsUpdated();
    }

    public void UpdateSystemPrompt(string systemPrompt)
    {
        SystemPrompt = systemPrompt;
        MarkAsUpdated();
    }

    public void UpdateVoice(string voiceName)
    {
        VoiceName = voiceName;
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

    public void SetAsDefault()
    {
        IsDefault = true;
        MarkAsUpdated();
    }

    public void UnsetDefault()
    {
        IsDefault = false;
        MarkAsUpdated();
    }
}
