using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace DesktopAssistant.UI.Views;

public partial class TextInputWithButtonView : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<TextInputWithButtonView, string?>(nameof(Text), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<TextInputWithButtonView, string?>(nameof(Watermark));

    public static readonly StyledProperty<ICommand?> SendCommandProperty =
        AvaloniaProperty.Register<TextInputWithButtonView, ICommand?>(nameof(SendCommand));

    public static readonly StyledProperty<bool> IsSendEnabledProperty =
        AvaloniaProperty.Register<TextInputWithButtonView, bool>(nameof(IsSendEnabled), defaultValue: true);

    public static readonly StyledProperty<double> MinInputHeightProperty =
        AvaloniaProperty.Register<TextInputWithButtonView, double>(nameof(MinInputHeight), defaultValue: 40);

    public static readonly StyledProperty<double> MaxInputHeightProperty =
        AvaloniaProperty.Register<TextInputWithButtonView, double>(nameof(MaxInputHeight), defaultValue: 120);

    public static readonly StyledProperty<VerticalAlignment> InputVerticalContentAlignmentProperty =
        AvaloniaProperty.Register<TextInputWithButtonView, VerticalAlignment>(nameof(InputVerticalContentAlignment), defaultValue: VerticalAlignment.Top);

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? Watermark { get => GetValue(WatermarkProperty); set => SetValue(WatermarkProperty, value); }
    public ICommand? SendCommand { get => GetValue(SendCommandProperty); set => SetValue(SendCommandProperty, value); }
    public bool IsSendEnabled { get => GetValue(IsSendEnabledProperty); set => SetValue(IsSendEnabledProperty, value); }
    public double MinInputHeight { get => GetValue(MinInputHeightProperty); set => SetValue(MinInputHeightProperty, value); }
    public double MaxInputHeight { get => GetValue(MaxInputHeightProperty); set => SetValue(MaxInputHeightProperty, value); }
    public VerticalAlignment InputVerticalContentAlignment { get => GetValue(InputVerticalContentAlignmentProperty); set => SetValue(InputVerticalContentAlignmentProperty, value); }

    public TextInputWithButtonView()
    {
        InitializeComponent();
    }

    private void TextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        if (e.KeyModifiers == KeyModifiers.Shift)
        {
            if (sender is TextBox textBox)
            {
                var caret = textBox.CaretIndex;
                textBox.Text = textBox.Text is null
                    ? "\n"
                    : textBox.Text.Insert(caret, "\n");
                textBox.CaretIndex = caret + 1;
            }
        }
        else if (e.KeyModifiers == KeyModifiers.None)
        {
            if (SendCommand?.CanExecute(null) == true)
                SendCommand.Execute(null);
        }

        e.Handled = true;
    }
}
