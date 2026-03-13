using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DesktopAssistant.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            UpdateMaxRestoreIcon();
            // Компенсируем невидимую рамку Windows при развёрнутом окне
            RootGrid.Margin = WindowState == WindowState.Maximized
                ? OffScreenMargin
                : default;
        }
        else if (change.Property == OffScreenMarginProperty)
        {
            if (WindowState == WindowState.Maximized)
                RootGrid.Margin = OffScreenMargin;
        }
    }

    private void UpdateMaxRestoreIcon()
    {
        if (MaxRestoreIcon is null) return;
        // □ — развернуть, ❐ — восстановить
        MaxRestoreIcon.Text = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount >= 2)
            ToggleMaximize();
        else
            BeginMoveDrag(e);
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
        => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
}
