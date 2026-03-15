using Avalonia.Controls;
using Avalonia.Threading;

namespace DesktopAssistant.UI.Views;

public partial class ChatView : UserControl
{
    private const double BottomThreshold = 2.0;

    // Scroll state per ChatViewModel
    private readonly Dictionary<object, ScrollState> _scrollStates = new();
    private bool _isAtBottom = true;
    private object? _activeContext;

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
        if (_activeContext != null)
            _scrollStates[_activeContext] = new ScrollState(sv.Offset.Y, _isAtBottom);

        _activeContext = DataContext;

        if (DataContext != null && _scrollStates.TryGetValue(DataContext, out var saved))
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
