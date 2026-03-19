using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace DesktopAssistant.UI.Views;

public partial class RegularToolCallView : UserControl
{
    public static readonly StyledProperty<ICommand?> SummarizeCommandProperty =
        AvaloniaProperty.Register<RegularToolCallView, ICommand?>(nameof(SummarizeCommand));

    public static readonly StyledProperty<ICommand?> ApproveToolCommandProperty =
        AvaloniaProperty.Register<RegularToolCallView, ICommand?>(nameof(ApproveToolCommand));

    public static readonly StyledProperty<ICommand?> DenyToolCommandProperty =
        AvaloniaProperty.Register<RegularToolCallView, ICommand?>(nameof(DenyToolCommand));

    public ICommand? SummarizeCommand
    {
        get => GetValue(SummarizeCommandProperty);
        set => SetValue(SummarizeCommandProperty, value);
    }

    public ICommand? ApproveToolCommand
    {
        get => GetValue(ApproveToolCommandProperty);
        set => SetValue(ApproveToolCommandProperty, value);
    }

    public ICommand? DenyToolCommand
    {
        get => GetValue(DenyToolCommandProperty);
        set => SetValue(DenyToolCommandProperty, value);
    }

    public RegularToolCallView()
    {
        InitializeComponent();
    }
}
