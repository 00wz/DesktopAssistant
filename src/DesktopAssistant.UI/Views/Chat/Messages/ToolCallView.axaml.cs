using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace DesktopAssistant.UI.Views;

public partial class ToolCallView : UserControl
{
    public static readonly StyledProperty<ICommand?> SummarizeCommandProperty =
        AvaloniaProperty.Register<ToolCallView, ICommand?>(nameof(SummarizeCommand));

    public static readonly StyledProperty<ICommand?> ApproveToolCommandProperty =
        AvaloniaProperty.Register<ToolCallView, ICommand?>(nameof(ApproveToolCommand));

    public static readonly StyledProperty<ICommand?> DenyToolCommandProperty =
        AvaloniaProperty.Register<ToolCallView, ICommand?>(nameof(DenyToolCommand));

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

    public ToolCallView()
    {
        InitializeComponent();
    }
}
