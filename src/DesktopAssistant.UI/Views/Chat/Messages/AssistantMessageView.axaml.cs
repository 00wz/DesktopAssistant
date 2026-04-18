using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace DesktopAssistant.UI.Views;

public partial class AssistantMessageView : UserControl
{
    public static readonly StyledProperty<ICommand?> SummarizeCommandProperty =
        AvaloniaProperty.Register<AssistantMessageView, ICommand?>(nameof(SummarizeCommand));

    public static readonly StyledProperty<ICommand?> NavigateToPreviousSiblingCommandProperty =
        AvaloniaProperty.Register<AssistantMessageView, ICommand?>(nameof(NavigateToPreviousSiblingCommand));

    public static readonly StyledProperty<ICommand?> NavigateToNextSiblingCommandProperty =
        AvaloniaProperty.Register<AssistantMessageView, ICommand?>(nameof(NavigateToNextSiblingCommand));

    public ICommand? SummarizeCommand
    {
        get => GetValue(SummarizeCommandProperty);
        set => SetValue(SummarizeCommandProperty, value);
    }

    public ICommand? NavigateToPreviousSiblingCommand
    {
        get => GetValue(NavigateToPreviousSiblingCommandProperty);
        set => SetValue(NavigateToPreviousSiblingCommandProperty, value);
    }

    public ICommand? NavigateToNextSiblingCommand
    {
        get => GetValue(NavigateToNextSiblingCommandProperty);
        set => SetValue(NavigateToNextSiblingCommandProperty, value);
    }

    public AssistantMessageView()
    {
        InitializeComponent();
    }
}
