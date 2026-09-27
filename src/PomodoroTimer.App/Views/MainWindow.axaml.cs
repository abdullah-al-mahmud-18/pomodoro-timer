using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace PomodoroTimer.App.Views;

public partial class MainWindow : Window
{
    private const string DigitsOnlyClass = "digitsOnly";

    public MainWindow()
    {
        InitializeComponent();

        // Tunnel so non-digit keystrokes are swallowed before the TextBox inserts them.
        AddHandler(TextInputEvent, OnDigitsOnlyTextInput, RoutingStrategies.Tunnel);
        AddHandler(TextBox.PastingFromClipboardEvent, OnDigitsOnlyPasting);
    }

    private static TextBox? DigitsOnlyTextBox(object? source)
    {
        var textBox = (source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true);
        return textBox is not null && textBox.Classes.Contains(DigitsOnlyClass) ? textBox : null;
    }

    private static void OnDigitsOnlyTextInput(object? sender, TextInputEventArgs e)
    {
        if (DigitsOnlyTextBox(e.Source) is not null && e.Text is { } text && !text.All(char.IsAsciiDigit))
        {
            e.Handled = true;
        }
    }

    /// <summary>Replaces the default paste with one that inserts only the digits from the clipboard text.</summary>
    private async void OnDigitsOnlyPasting(object? sender, RoutedEventArgs e)
    {
        if (DigitsOnlyTextBox(e.Source) is not { } textBox)
        {
            return;
        }

        e.Handled = true;

        var clipboard = TopLevel.GetTopLevel(textBox)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        var digits = new string((await clipboard.GetTextAsync() ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length > 0)
        {
            textBox.SelectedText = digits;
        }
    }
}
