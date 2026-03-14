using Avalonia.Controls;
using Avalonia.Input;
using DesktopAssistant.UI.ViewModels;

namespace DesktopAssistant.UI.Views;

public partial class NewConversationPanelView : UserControl
{
    public NewConversationPanelView()
    {
        InitializeComponent();
    }

    private void FirstMessageBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        if (e.KeyModifiers == KeyModifiers.Shift)
        {
            // Shift+Enter — вставить перенос строки
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
            // Enter — отправить сообщение
            if (DataContext is NewConversationPanelViewModel vm &&
                vm.ConfirmCommand.CanExecute(null))
                vm.ConfirmCommand.Execute(null);
        }

        e.Handled = true;
    }
}
