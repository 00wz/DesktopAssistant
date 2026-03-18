using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.Application.Dtos;

/// <summary>Assistant profile DTO. HasApiKey indicates whether a key exists in the secure store.</summary>
public record AssistantProfileDto(
    Guid Id,
    string Description,
    string BaseUrl,
    string ModelId,
    double Temperature,
    int MaxTokens,
    bool IsDefault,
    bool HasApiKey,
    bool IsSummarizationProfile);

/// <summary>Settings for a specific conversation: system prompt, profile, and mode.</summary>
public record ConversationSettingsDto(
    Guid ConversationId,
    string SystemPrompt,
    Guid? AssistantProfileId,
    AssistantProfileDto? Profile,
    ConversationMode Mode = ConversationMode.Chat);
