using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

static class Dialogs
{
    private static Window NewDialog(double scale, Control content)
    {
        var window = new Window
        {
            Title = "Wiim Control",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(scale, scale),
                Child = new Border { Padding = new Thickness(20, 18), Child = content }
            }
        };
        WindowChrome.Apply(window);
        return window;
    }

    private static async Task<T> ShowAsync<T>(Window dialog, Window? owner, Func<T> result)
    {
        if (owner != null && owner.IsVisible)
        {
            await dialog.ShowDialog(owner);
            return result();
        }
        var closed = new TaskCompletionSource();
        dialog.Closed += (_, _) => closed.TrySetResult();
        dialog.Show();
        dialog.Activate();
        await closed.Task;
        return result();
    }

    private static StackPanel ButtonRow(params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Margin = new Thickness(0, 16, 0, 0)
        };
        foreach (var b in buttons) row.Children.Add(b);
        return row;
    }

    private static Button DialogButton(string text, bool accent = false)
    {
        var button = new Button { Content = text, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Classes.Add("pill");
        if (accent) button.Classes.Add("accent");
        return button;
    }

    private static TextBlock Message(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 440
    };

    public static Task MessageAsync(Window? owner, string message, double scale = 1)
    {
        var ok = DialogButton("OK", accent: true);
        ok.IsDefault = true;
        ok.IsCancel = true;
        var window = NewDialog(scale, new StackPanel { Children = { Message(message), ButtonRow(ok) } });
        ok.Click += (_, _) => window.Close();
        return ShowAsync(window, owner, () => true);
    }

    public static Task<bool> ConfirmAsync(Window? owner, string message, double scale = 1)
    {
        bool answer = false;
        var yes = DialogButton("Yes", accent: true);
        var no = DialogButton("No");
        yes.IsDefault = true;
        no.IsCancel = true;
        var window = NewDialog(scale, new StackPanel { Children = { Message(message), ButtonRow(yes, no) } });
        yes.Click += (_, _) => { answer = true; window.Close(); };
        no.Click += (_, _) => window.Close();
        return ShowAsync(window, owner, () => answer);
    }

    public static Task<string?> PromptAsync(Window? owner, string message, string initialText = "", double scale = 1)
    {
        string? answer = null;
        var input = new TextBox { Text = initialText, Width = 340, Margin = new Thickness(0, 10, 0, 0) };
        var ok = DialogButton("OK", accent: true);
        var cancel = DialogButton("Cancel");
        ok.IsDefault = true;
        cancel.IsCancel = true;
        var window = NewDialog(scale, new StackPanel { Children = { Message(message), input, ButtonRow(ok, cancel) } });
        ok.Click += (_, _) => { answer = input.Text?.Trim(); window.Close(); };
        cancel.Click += (_, _) => window.Close();
        window.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        return ShowAsync(window, owner, () => answer);
    }

    public static async Task<KnownDevice?> PickDeviceAsync(List<KnownDevice> devices, double scale = 1)
    {
        if (devices.Count == 0)
        {
            var ip = await PromptAsync(null, "No Wiim devices found automatically.\nEnter your Wiim Amp IP address:", string.Empty, scale);
            return string.IsNullOrWhiteSpace(ip) ? null : new KnownDevice("Wiim Amp", ip, string.Empty);
        }

        KnownDevice? answer = null;
        bool manual = false;
        var list = new ListBox { Width = 360, Height = 160, Margin = new Thickness(0, 10, 0, 0), Background = Palette.Input };
        foreach (var d in devices) list.Items.Add($"{d.Name} ({d.Ip})");
        list.SelectedIndex = 0;
        var ok = DialogButton("OK", accent: true);
        var enterManually = DialogButton("Enter manually…");
        var cancel = DialogButton("Cancel");
        ok.IsDefault = true;
        cancel.IsCancel = true;
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(enterManually);
        var right = ButtonRow(ok, cancel);
        right.Margin = new Thickness(0);
        Grid.SetColumn(right, 1);
        buttons.Children.Add(right);
        var window = NewDialog(scale, new StackPanel { Children = { Message("Select your Wiim device:"), list, buttons } });
        ok.Click += (_, _) => { if (list.SelectedIndex >= 0) answer = devices[list.SelectedIndex]; window.Close(); };
        enterManually.Click += (_, _) => { manual = true; window.Close(); };
        cancel.Click += (_, _) => window.Close();
        await ShowAsync(window, null, () => true);

        if (!manual) return answer;
        var manualIp = await PromptAsync(null, "Enter your Wiim Amp IP address:", string.Empty, scale);
        return string.IsNullOrWhiteSpace(manualIp) ? null : new KnownDevice("Wiim Amp", manualIp, string.Empty);
    }
}
