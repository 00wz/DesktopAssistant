using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace DesktopAssistant.UI.Views;

public partial class UserMessageView : UserControl
{
    public static readonly StyledProperty<ICommand?> SummarizeCommandProperty =
        AvaloniaProperty.Register<UserMessageView, ICommand?>(nameof(SummarizeCommand));

    public static readonly StyledProperty<ICommand?> NavigateToPreviousSiblingCommandProperty =
        AvaloniaProperty.Register<UserMessageView, ICommand?>(nameof(NavigateToPreviousSiblingCommand));

    public static readonly StyledProperty<ICommand?> NavigateToNextSiblingCommandProperty =
        AvaloniaProperty.Register<UserMessageView, ICommand?>(nameof(NavigateToNextSiblingCommand));

    public static readonly StyledProperty<ICommand?> StartEditMessageCommandProperty =
        AvaloniaProperty.Register<UserMessageView, ICommand?>(nameof(StartEditMessageCommand));

    public static readonly StyledProperty<ICommand?> SaveEditedMessageCommandProperty =
        AvaloniaProperty.Register<UserMessageView, ICommand?>(nameof(SaveEditedMessageCommand));

    public static readonly StyledProperty<ICommand?> CancelEditMessageCommandProperty =
        AvaloniaProperty.Register<UserMessageView, ICommand?>(nameof(CancelEditMessageCommand));

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

    public ICommand? StartEditMessageCommand
    {
        get => GetValue(StartEditMessageCommandProperty);
        set => SetValue(StartEditMessageCommandProperty, value);
    }

    public ICommand? SaveEditedMessageCommand
    {
        get => GetValue(SaveEditedMessageCommandProperty);
        set => SetValue(SaveEditedMessageCommandProperty, value);
    }

    public ICommand? CancelEditMessageCommand
    {
        get => GetValue(CancelEditMessageCommandProperty);
        set => SetValue(CancelEditMessageCommandProperty, value);
    }

    public UserMessageView()
    {
        InitializeComponent();
    }
}
