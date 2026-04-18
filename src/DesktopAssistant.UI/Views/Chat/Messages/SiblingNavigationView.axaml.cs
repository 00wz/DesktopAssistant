using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace DesktopAssistant.UI.Views;

public partial class SiblingNavigationView : UserControl
{
    public static readonly StyledProperty<ICommand?> NavigateToPreviousSiblingCommandProperty =
        AvaloniaProperty.Register<SiblingNavigationView, ICommand?>(nameof(NavigateToPreviousSiblingCommand));

    public static readonly StyledProperty<ICommand?> NavigateToNextSiblingCommandProperty =
        AvaloniaProperty.Register<SiblingNavigationView, ICommand?>(nameof(NavigateToNextSiblingCommand));

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

    public SiblingNavigationView()
    {
        InitializeComponent();
    }
}
