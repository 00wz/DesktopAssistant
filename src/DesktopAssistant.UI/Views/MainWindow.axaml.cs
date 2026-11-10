using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DesktopAssistant.UI.ViewModels;

namespace DesktopAssistant.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Global keyboard shortcuts: Ctrl+N new chat, Ctrl+, settings, Ctrl+1..3
    /// settings sections, Ctrl+B toggle sidebar and F1 help.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (e.Key == Key.F1 && e.KeyModifiers == KeyModifiers.None)
        {
            _ = vm.OpenSettingsSectionAsync(SettingsSection.Help);
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        switch (e.Key)
        {
            case Key.N:
                vm.CreateNewChatCommand.Execute(null);
                break;
            case Key.OemComma:
                vm.OpenSettingsCommand.Execute(null);
                break;
            case Key.B:
                vm.ToggleSidebarCommand.Execute(null);
                break;
            case Key.D1:
            case Key.NumPad1:
                _ = vm.OpenSettingsSectionAsync(SettingsSection.General);
                break;
            case Key.D2:
            case Key.NumPad2:
                _ = vm.OpenSettingsSectionAsync(SettingsSection.Profiles);
                break;
            case Key.D3:
            case Key.NumPad3:
                _ = vm.OpenSettingsSectionAsync(SettingsSection.ToolApproval);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            UpdateMaxRestoreIcon();
            // Compensate for the invisible Windows border when the window is maximized
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
        // □ — maximize, ❐ — restore
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
