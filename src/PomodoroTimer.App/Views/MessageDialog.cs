using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PomodoroTimer.App.Views;

/// <summary>
/// Small modal message box (Avalonia has none built in). Buttons are listed left to right; the result is the
/// index of the one clicked, or null if the window was closed another way.
/// </summary>
public class MessageDialog : Window
{
    public MessageDialog(string title, string message, IReadOnlyList<string> buttons, string? selectableText = null)
    {
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 16 };
        content.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });

        if (selectableText is not null)
        {
            content.Children.Add(new TextBox { Text = selectableText, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 120 });
        }

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], Padding = new Avalonia.Thickness(12, 6) };
            button.Click += (_, _) => Close(index);
            buttonRow.Children.Add(button);
        }
        content.Children.Add(buttonRow);

        Content = content;
    }

    public static Task<int?> ShowAsync(Window owner, string title, string message, params string[] buttons) =>
        new MessageDialog(title, message, buttons).ShowDialog<int?>(owner);
}
