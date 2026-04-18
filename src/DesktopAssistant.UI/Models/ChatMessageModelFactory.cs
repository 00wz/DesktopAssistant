using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// Factory for creating ChatMessageModel subtypes from DTOs.
/// The single mapping point for DTO → UI model conversions.
/// </summary>
public static class ChatMessageModelFactory
{
    public static ChatMessageModel FromDto(MessageDto dto)
    {
        ChatMessageModel model = dto switch
        {
            UserMessageDto u => new UserChatMessageModel(u.Id, u.Content, u.CreatedAt),
            AssistantMessageDto a => new AssistantChatMessageModel(a.Id, a.Content, a.CreatedAt),
            ToolResultDto t when t.IsTerminal => new AgentResultModel
            {
                Id = t.Id,
                CreatedAt = t.CreatedAt,
                CallId = t.CallId,
                PluginName = t.PluginName,
                FunctionName = t.FunctionName,
                ResultJson = t.ResultJson,
                Status = MapStatus(t.Status),
                Message = AgentResultModel.ExtractMessage(t.ArgumentsJson)
            },
            ToolResultDto t => new RegularToolCallModel
            {
                Id = t.Id,
                CreatedAt = t.CreatedAt,
                CallId = t.CallId,
                PluginName = t.PluginName,
                FunctionName = t.FunctionName,
                ArgumentsJson = t.ArgumentsJson,
                ResultJson = t.ResultJson,
                Status = MapStatus(t.Status)
            },
            SummaryMessageDto s => new SummarizationChatMessageModel
            {
                Id = s.Id,
                CreatedAt = s.CreatedAt,
                SummaryContent = s.SummaryContent,
                Status = SummarizationStatus.Completed
            },
            _ => throw new ArgumentOutOfRangeException(nameof(dto), $"Unknown DTO type: {dto.GetType().Name}")
        };

        model.ParentId = dto.ParentId;
        model.CurrentSiblingIndex = dto.CurrentSiblingIndex;
        model.TotalSiblings = dto.TotalSiblings;
        model.HasPreviousSibling = dto.HasPreviousSibling;
        model.HasNextSibling = dto.HasNextSibling;
        model.PreviousSiblingId = dto.PreviousSiblingId;
        model.NextSiblingId = dto.NextSiblingId;

        return model;
    }

    private static ToolCallStatus MapStatus(ToolNodeStatus s) => s switch
    {
        ToolNodeStatus.Completed => ToolCallStatus.Completed,
        ToolNodeStatus.Failed    => ToolCallStatus.Failed,
        ToolNodeStatus.Denied    => ToolCallStatus.Denied,
        _                       => ToolCallStatus.Pending
    };
}
