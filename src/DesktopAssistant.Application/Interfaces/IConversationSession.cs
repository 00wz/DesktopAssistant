using DesktopAssistant.Application.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DesktopAssistant.Application.Interfaces;

public interface IConversationSession : IDisposable
{
    /// <summary>
    /// Id диалога
    /// </summary>
    Guid ConversationId { get; }

    /// <summary>
    /// Состояние диалога
    /// </summary>
    ConversationState State { get; }

    /// <summary>
    /// Выполняется ли запрос llm в данный момент
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Событие диалога
    /// </summary>
    event EventHandler<SessionEvent> EventOccurred;

    // Data facade (над IChatService — ChatViewModel не зависит от IChatService)
    //Task<ConversationDto?> GetConversationAsync(CancellationToken ct = default);
    Task<IEnumerable<MessageDto>> LoadHistoryAsync(CancellationToken ct = default);
    // → обновляет _leafNodeId + State, стреляет ConversationStateChangedSessionEvent
    Task<ConversationSettingsDto?> GetSettingsAsync(CancellationToken ct = default);
    Task UpdateSystemPromptAsync(string systemPrompt, CancellationToken ct = default);
    Task ChangeProfileAsync(Guid profileId, CancellationToken ct = default);
    Task SummarizeAsync(Guid selectedNodeId, CancellationToken ct = default);
    Task SwitchToSiblingAsync(Guid parentNodeId, Guid newChildId, CancellationToken ct = default);

    // LLM operations
    Task SendMessageAsync(string message, Guid? parentNodeId = null, CancellationToken ct = default);
    Task ResumeAsync(CancellationToken ct = default);
    Task ApproveToolAsync(Guid pendingNodeId, CancellationToken ct = default);
    Task DenyToolAsync(Guid pendingNodeId, CancellationToken ct = default);
}
