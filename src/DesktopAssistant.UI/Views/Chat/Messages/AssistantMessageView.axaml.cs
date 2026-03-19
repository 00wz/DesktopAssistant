using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace DesktopAssistant.UI.Views;

public partial class AssistantMessageView : UserControl
{
    public static readonly StyledProperty<ICommand?> SummarizeCommandProperty =
        AvaloniaProperty.Register<AssistantMessageView, ICommand?>(nameof(SummarizeCommand));

    public ICommand? SummarizeCommand
    {
        get => GetValue(SummarizeCommandProperty);
        set => SetValue(SummarizeCommandProperty, value);
    }

    public AssistantMessageView()
    {
        InitializeComponent();
    }
}
