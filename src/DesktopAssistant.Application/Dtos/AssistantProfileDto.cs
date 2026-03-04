namespace DesktopAssistant.Application.Dtos;

/// <summary>DTO профиля ассистента. HasApiKey — признак наличия ключа в защищённом хранилище.</summary>
public record AssistantProfileDto(
    Guid Id,
    string Name,
    string BaseUrl,
    string ModelId,
    double Temperature,
    int MaxTokens,
    bool IsDefault,
    bool HasApiKey,
    bool IsSummarizationProfile);

/// <summary>Настройки конкретного диалога: системный промпт и профиль.</summary>
public record ConversationSettingsDto(
    Guid ConversationId,
    string SystemPrompt,
    Guid AssistantProfileId,
    AssistantProfileDto Profile);
