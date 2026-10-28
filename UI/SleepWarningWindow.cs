using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace WiimControl;

sealed class SleepWarningWindow : Window
{
    private const double ToastWidth = 340;
    private const int StackGap = 10;

    private readonly TextBlock _title = new()
    {
        FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly TextBlock _countdown = new() { FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(214, 214, 214)), Margin = new Thickness(0, 2, 0, 0) };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ScaledContent _scaler = new();
    private readonly DateTime _sleepsAt;
    private OsdCorner _corner;
    private int _slot;
    private double _room;

    public SleepWarningWindow(string deviceName, int seconds, bool canChange, OsdCorner corner, double scale, int slot, double room,
        Func<Task> addTime, Func<Task> stopTimer, Action dismissed)
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        _sleepsAt = DateTime.UtcNow.AddSeconds(seconds);
        _corner = corner;
        _slot = slot;
        _room = room;

        _title.Text = $"{deviceName} goes to sleep soon";
        var close = new Button
        {
            Content = new PathIcon { Data = Icons.Close, Width = 10, Height = 10 }, Padding = new Thickness(6), MinHeight = 0, MinWidth = 0,
            Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Top
        };
        ToolTip.SetTip(close, "Hide this message");
        close.Click += (_, _) =>
        {
            dismissed();
            Close();
        };
        var header = new DockPanel { Children = { close, new StackPanel { Children = { _title, _countdown } } } };
        DockPanel.SetDock(close, Dock.Right);

        Button Action(string text, Func<Task> run)
        {
            var button = new Button { Content = text, IsEnabled = canChange };
            button.Classes.Add("pill");
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                await run();
                Close();
            };
            return button;
        }
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0),
            Children = { Action("Add 15 minutes", addTime), Action("Stop timer", stopTimer) }
        };

        _scaler.Scale = scale;
        _scaler.Child = new Border
        {
            Width = ToastWidth, Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)), BorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
            BorderThickness = new Thickness(1), Child = new StackPanel { Children = { header, buttons } }
        };
        Content = _scaler;

        _clock.Tick += (_, _) => Tick();
        Tick();
        _clock.Start();
        Opened += (_, _) => { WindowChrome.MakeToolWindow(this); Reposition(); };
        SizeChanged += (_, _) => Reposition();
        Closed += (_, _) => _clock.Stop();
    }

    public int Slot
    {
        get => _slot;
        set
        {
            _slot = value;
            Reposition();
        }
    }

    public double Room
    {
        set
        {
            _room = value;
            Reposition();
        }
    }

    private void Tick()
    {
        int left = (int)Math.Ceiling((_sleepsAt - DateTime.UtcNow).TotalSeconds);
        if (left <= 0)
        {
            Close();
            return;
        }
        _countdown.Text = $"Sleep timer ends in {left / 60}:{left % 60:00}";
    }

    private void Reposition()
    {
        var screen = Screens.Primary;
        if (screen == null) return;
        var area = screen.WorkingArea;
        double scaling = screen.Scaling;
        int width = (int)Math.Ceiling(Bounds.Width * scaling);
        int height = (int)Math.Ceiling(Bounds.Height * scaling);
        int margin = (int)(24 * scaling);
        int shift = _slot * (height + (int)(StackGap * scaling)) + (int)(_room * scaling);
        Position = _corner switch
        {
            OsdCorner.TopLeft    => new PixelPoint(area.X + margin, area.Y + margin + shift),
            OsdCorner.TopRight   => new PixelPoint(area.Right - width - margin, area.Y + margin + shift),
            OsdCorner.BottomLeft => new PixelPoint(area.X + margin, area.Bottom - height - margin - shift),
            _                    => new PixelPoint(area.Right - width - margin, area.Bottom - height - margin - shift)
        };
    }
}
