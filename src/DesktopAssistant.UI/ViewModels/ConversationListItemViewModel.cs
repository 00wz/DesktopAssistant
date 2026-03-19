using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Application.Dtos;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.UI.Models;
using System.Collections.ObjectModel;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel for an item in the conversation list.
/// Wraps <see cref="ConversationListItem"/> and tracks the active session status in real time.
/// </summary>
public partial class ConversationListItemViewModel : ObservableObject, IDisposable
{
    private readonly ConversationListItem _model;
    private IConversationSession? _session;

    public Guid Id => _model.Id;
    public Guid? ParentId => _model.ParentId;
    public string Title => _model.Title;

    /// <summary>Human-readable relative timestamp of the last update (e.g. "today, 14:32").</summary>
    public string FormattedDate
    {
        get
        {
            var local = _model.UpdatedAt.ToLocalTime();
            var now = DateTime.Now;
            var diff = now - local;

            if (diff.TotalMinutes < 1)  return "just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min. ago";
            if (diff.TotalHours < 24 && local.Date == now.Date)
                return $"today, {local:HH:mm}";
            if (diff.TotalHours < 48 && local.Date == now.Date.AddDays(-1))
                return $"yesterday, {local:HH:mm}";
            if (diff.TotalDays < 7) return local.ToString("dddd, HH:mm");
            return local.ToString("d MMMM, HH:mm");
        }
    }

    /// <summary>
    /// Nesting depth in the conversation tree (0 = root).
    /// Controls the left indentation in <see cref="ConversationSidebarView"/>.
    /// </summary>
    public int Depth { get; init; }

    /// <summary>
    /// Margin for the item row content: left edge grows 16px per nesting level,
    /// so child conversations appear visually indented under their parent.
    /// </summary>
    public Thickness DepthMargin => new Thickness(12 + Depth * 16.0, 10, 12, 10);

    /// <summary>Child conversations nested under this one.</summary>
    public ObservableCollection<ConversationListItemViewModel> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;

    /// <summary>True if an active <see cref="IConversationSession"/> exists for this conversation.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>True if the conversation is open in the main area.</summary>
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isExecutingTools;

    [ObservableProperty]
    private ConversationState _sessionState = ConversationState.LastMessageIsAssistant;

    // ── Computed icons — direct binding in XAML ───────────────────────────────

    public bool IsLoadingIcon  => IsActive && (IsRunning || IsExecutingTools);

    public bool IsPausedIcon   => IsActive && !IsRunning && !IsExecutingTools &&
        SessionState is ConversationState.LastMessageIsUser
                      or ConversationState.AllToolCallsCompleted;

    public bool IsQuestionIcon => IsActive && !IsRunning && !IsExecutingTools &&
        SessionState == ConversationState.HasPendingToolCalls;

    public bool IsErrorIcon    => IsActive && !IsRunning && !IsExecutingTools &&
        SessionState == ConversationState.ToolCallIdMismatch;

    public ConversationListItemViewModel(ConversationListItem model)
    {
        _model = model;
    }

    /// <summary>Subscribes to session events and starts tracking its state.</summary>
    public void AttachSession(IConversationSession session)
    {
        if (_session != null)
            DetachSession();

        _session = session;
        IsRunning = session.IsRunning;
        IsExecutingTools = session.IsExecutingTools;
        SessionState = session.State;
        IsActive = true;

        session.EventOccurred += OnSessionEvent;
    }

    /// <summary>Unsubscribes from session events and resets state.</summary>
    public void DetachSession()
    {
        if (_session is null) return;

        _session.EventOccurred -= OnSessionEvent;
        _session = null;
        IsActive = false;
        IsRunning = false;
        IsExecutingTools = false;
    }

    private void OnSessionEvent(object? sender, SessionEvent e)
    {
        Dispatcher.UIThread.Post(() => HandleEvent(e));
    }

    private void HandleEvent(SessionEvent e)
    {
        switch (e)
        {
            case RunningStateChangedSessionEvent ev:
                IsRunning = ev.IsRunning;
                break;
            case ConversationStateChangedSessionEvent ev:
                SessionState = ev.State;
                break;
            case ToolExecutionStateChangedSessionEvent ev:
                IsExecutingTools = ev.IsExecutingTools;
                break;
        }
    }

    // Recompute derived icon properties whenever any dependent field changes
    partial void OnIsActiveChanged(bool value)             => NotifyIconProperties();
    partial void OnIsRunningChanged(bool value)            => NotifyIconProperties();
    partial void OnIsExecutingToolsChanged(bool value)     => NotifyIconProperties();
    partial void OnSessionStateChanged(ConversationState value) => NotifyIconProperties();

    private void NotifyIconProperties()
    {
        OnPropertyChanged(nameof(IsLoadingIcon));
        OnPropertyChanged(nameof(IsPausedIcon));
        OnPropertyChanged(nameof(IsQuestionIcon));
        OnPropertyChanged(nameof(IsErrorIcon));
    }

    public void Dispose() => DetachSession();
}
