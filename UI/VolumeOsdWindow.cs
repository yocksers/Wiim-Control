using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace WiimControl;

sealed class VolumeOsdWindow : Window
{
    private const double BarWidth = 260;
    private readonly TextBlock _label = new()
    {
        FontSize = 15, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center,
        TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = BarWidth
    };
    private readonly Border _barFill = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(230, 0, 153, 255)), CornerRadius = new CornerRadius(7),
        HorizontalAlignment = HorizontalAlignment.Left, Height = 14
    };
    private readonly Border _bar;
    private readonly TextBlock _caption = new()
    {
        FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)), HorizontalAlignment = HorizontalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = BarWidth, Margin = new Thickness(0, 10, 0, 0)
    };
    private readonly DispatcherTimer _hideTimer = new();
    private OsdCorner _corner;

    public VolumeOsdWindow()
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _bar = new Border
        {
            Width = BarWidth, Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 10, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), Child = _barFill
        };
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20, 14),
            MinWidth = 240,
            Child = new StackPanel { Children = { _label, _bar, _caption } }
        };

        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
        Opened += (_, _) => { WindowChrome.MakeToolWindow(this); Reposition(); };
        SizeChanged += (_, _) => Reposition();
    }

    public void ShowVolume(int volume, bool muted, OsdCorner corner, int durationMs, string caption)
    {
        _label.Text = muted ? "Muted" : $"Volume {volume}%";
        _bar.IsVisible = true;
        _barFill.IsVisible = !muted && volume > 0;
        _barFill.Width = Math.Max(14, BarWidth * Math.Clamp(volume, 0, 100) / 100.0);
        _caption.Text = caption.Length > 0 ? "♪  " + caption : string.Empty;
        _caption.IsVisible = caption.Length > 0;
        Present(corner, durationMs);
    }

    public void ShowMessage(string message, OsdCorner corner, int durationMs)
    {
        _label.Text = message;
        _bar.IsVisible = false;
        _caption.IsVisible = false;
        Present(corner, durationMs);
    }

    private void Present(OsdCorner corner, int durationMs)
    {
        _corner = corner;
        if (!IsVisible) Show();
        Reposition();
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(200, durationMs));
        _hideTimer.Start();
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
        Position = _corner switch
        {
            OsdCorner.TopLeft    => new PixelPoint(area.X + margin, area.Y + margin),
            OsdCorner.TopRight   => new PixelPoint(area.Right - width - margin, area.Y + margin),
            OsdCorner.BottomLeft => new PixelPoint(area.X + margin, area.Bottom - height - margin),
            _                    => new PixelPoint(area.Right - width - margin, area.Bottom - height - margin)
        };
    }
}
