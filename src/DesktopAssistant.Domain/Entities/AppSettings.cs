using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.Domain.Entities;

/// <summary>
/// Глобальные настройки приложения
/// </summary>
public class AppSettings : BaseEntity
{
    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private AppSettings() { } // Для EF Core

    public AppSettings(string key, string value, string? description = null)
    {
        Key = key;
        Value = value;
        Description = description;
    }

    public void UpdateValue(string value)
    {
        Value = value;
        MarkAsUpdated();
    }

    // Константы для ключей настроек
    public static class Keys
    {
        public const string DefaultProvider = "DefaultProvider";
        public const string DefaultModel = "DefaultModel";
        public const string ApiKeyOpenAI = "ApiKey:OpenAI";
        public const string ApiKeyAzureOpenAI = "ApiKey:AzureOpenAI";
        public const string AzureOpenAIEndpoint = "AzureOpenAI:Endpoint";
        public const string ApiKeyAnthropic = "ApiKey:Anthropic";
        public const string ApiKeyGoogle = "ApiKey:Google";
        public const string OllamaEndpoint = "Ollama:Endpoint";
        public const string TtsProvider = "TTS:Provider";
        public const string TtsVoice = "TTS:DefaultVoice";
        public const string AzureSpeechKey = "Azure:SpeechKey";
        public const string AzureSpeechRegion = "Azure:SpeechRegion";
        public const string VoskModelPath = "Speech:VoskModelPath";
        public const string SpeechLanguage = "Speech:Language";
        public const string SummarizationThreshold = "Summarization:TokenThreshold";
        public const string IsFirstRun = "App:IsFirstRun";
        public const string Theme = "App:Theme";
    }
}
