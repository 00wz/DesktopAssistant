using Avalonia.Controls;
using Avalonia.Threading;

namespace DesktopAssistant.UI.Views;

public partial class ChatView : UserControl
{
    private const double BottomThreshold = 2.0;
    private const int MaxScrollStates = 50;

    // Scroll state per ChatViewModel, keyed by conversation Guid.
    // Capped at MaxScrollStates to prevent unbounded growth over a long session.
    private readonly Dictionary<Guid, ScrollState> _scrollStates = new();
    private bool _isAtBottom = true;
    private Guid? _activeContextId;

    public ChatView()
    {
        InitializeComponent();

        var sv = this.FindControl<ScrollViewer>("MessagesScrollViewer")!;

        sv.Loaded += (_, _) => sv.ScrollToEnd();
        sv.ScrollChanged += OnScrollChanged;
        DataContextChanged += (_, _) => OnDataContextChanged(sv);
    }

    private void OnDataContextChanged(ScrollViewer sv)
    {
        // Save scroll state for the previous context
        if (_activeContextId.HasValue)
            _scrollStates[_activeContextId.Value] = new ScrollState(sv.Offset.Y, _isAtBottom);

        var vm = DataContext as ViewModels.ChatViewModel;
        _activeContextId = vm?.ConversationId;

        if (_activeContextId.HasValue && _scrollStates.TryGetValue(_activeContextId.Value, out var saved))
        {
            _isAtBottom = saved.IsAtBottom;
            // Restore after layout completes
            Dispatcher.UIThread.Post(
                () => sv.Offset = sv.Offset.WithY(saved.Offset),
                DispatcherPriority.Loaded);
        }
        else
        {
            _isAtBottom = true;
            // New chat — scroll to bottom will happen automatically when messages load
        }

        // Evict oldest entries when the cap is exceeded
        if (_scrollStates.Count > MaxScrollStates)
        {
            var oldest = _scrollStates.Keys.First();
            _scrollStates.Remove(oldest);
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var sv = (ScrollViewer)sender!;

        if (e.ExtentDelta.Y != 0)
        {
            if (_isAtBottom)
                sv.ScrollToEnd();
        }
        else if (e.OffsetDelta.Y != 0)
        {
            _isAtBottom = sv.Offset.Y + sv.Viewport.Height >= sv.Extent.Height - BottomThreshold;
        }
    }

    private readonly record struct ScrollState(double Offset, bool IsAtBottom);
}
