using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// Factory for creating ChatMessageModel subtypes from DTOs.
/// The single mapping point for DTO → UI model conversions.
/// </summary>
public static class ChatMessageModelFactory
{
    public static ChatMessageModel FromDto(MessageDto dto) => dto switch
    {
        UserMessageDto u => new UserChatMessageModel(u.Id, u.Content, u.CreatedAt)
        {
            ParentId = u.ParentId,
            CurrentSiblingIndex = u.CurrentSiblingIndex,
            TotalSiblings = u.TotalSiblings,
            HasPreviousSibling = u.HasPreviousSibling,
            HasNextSibling = u.HasNextSibling,
            PreviousSiblingId = u.PreviousSiblingId,
            NextSiblingId = u.NextSiblingId
        },

        AssistantMessageDto a => new AssistantChatMessageModel(a.Id, a.Content, a.CreatedAt)
        {
            ParentId = a.ParentId,
            CurrentSiblingIndex = a.CurrentSiblingIndex,
            TotalSiblings = a.TotalSiblings,
            HasPreviousSibling = a.HasPreviousSibling,
            HasNextSibling = a.HasNextSibling,
            PreviousSiblingId = a.PreviousSiblingId,
            NextSiblingId = a.NextSiblingId
        },

        ToolResultDto t => new ToolChatMessageModel
        {
            Id = t.Id,
            CreatedAt = t.CreatedAt,
            ParentId = t.ParentId,
            CallId = t.CallId,
            PluginName = t.PluginName,
            FunctionName = t.FunctionName,
            ArgumentsJson = t.ArgumentsJson,
            ResultJson = t.ResultJson,
            Status = t.Status switch
            {
                ToolNodeStatus.Completed => ToolCallStatus.Completed,
                ToolNodeStatus.Failed    => ToolCallStatus.Failed,
                ToolNodeStatus.Denied    => ToolCallStatus.Denied,
                _                       => ToolCallStatus.Pending
            }
        },

        SummaryMessageDto s => new SummarizationChatMessageModel
        {
            Id = s.Id,
            CreatedAt = s.CreatedAt,
            ParentId = s.ParentId,
            SummaryContent = s.SummaryContent,
            Status = SummarizationStatus.Completed
        },

        _ => throw new ArgumentOutOfRangeException(nameof(dto), $"Unknown DTO type: {dto.GetType().Name}")
    };
}
