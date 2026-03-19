using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace DesktopAssistant.UI.Views;

public partial class ToolApprovalButtonsView : UserControl
{
    public static readonly StyledProperty<ICommand?> ApproveCommandProperty =
        AvaloniaProperty.Register<ToolApprovalButtonsView, ICommand?>(nameof(ApproveCommand));

    public static readonly StyledProperty<ICommand?> DenyCommandProperty =
        AvaloniaProperty.Register<ToolApprovalButtonsView, ICommand?>(nameof(DenyCommand));

    public ICommand? ApproveCommand
    {
        get => GetValue(ApproveCommandProperty);
        set => SetValue(ApproveCommandProperty, value);
    }

    public ICommand? DenyCommand
    {
        get => GetValue(DenyCommandProperty);
        set => SetValue(DenyCommandProperty, value);
    }

    public ToolApprovalButtonsView()
    {
        InitializeComponent();
    }
}
