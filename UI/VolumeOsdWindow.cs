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
    private readonly ScaledContent _scaler = new();
    private OsdCorner _corner;
    private double _scale = 1;
    private bool _warming;

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
        _scaler.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20, 14),
            MinWidth = 240,
            Child = new StackPanel { Children = { _label, _bar, _caption } }
        };
        Content = _scaler;

        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
        Opened += (_, _) => { WindowChrome.MakeToolWindow(this); Reposition(); };
        SizeChanged += (_, _) => Reposition();
    }

    public void ShowVolume(int volume, bool muted, OsdCorner corner, int durationMs, string caption, double scale)
    {
        SetVolume(volume, muted, caption);
        Present(corner, durationMs, scale);
    }

    public void WarmUp(double scale)
    {
        if (IsVisible) return;
        _warming = true;
        SetVolume(50, false, "Wiim Control");
        _scale = scale;
        _scaler.Scale = scale;
        Position = new PixelPoint(-32000, -32000);
        Show();
        DispatcherTimer.RunOnce(EndWarmUp, TimeSpan.FromMilliseconds(500));
    }

    private void EndWarmUp()
    {
        if (!_warming) return;
        _warming = false;
        Hide();
    }

    private void SetVolume(int volume, bool muted, string caption)
    {
        _label.Text = muted ? "Muted" : $"Volume {volume}%";
        _bar.IsVisible = true;
        _barFill.IsVisible = !muted && volume > 0;
        _barFill.Width = Math.Max(14, BarWidth * Math.Clamp(volume, 0, 100) / 100.0);
        _caption.Text = caption.Length > 0 ? "♪  " + caption : string.Empty;
        _caption.IsVisible = caption.Length > 0;
    }

    public void ShowMessage(string message, OsdCorner corner, int durationMs, double scale)
    {
        _label.Text = message;
        _bar.IsVisible = false;
        _caption.IsVisible = false;
        Present(corner, durationMs, scale);
    }

    private void Present(OsdCorner corner, int durationMs, double scale)
    {
        _corner = corner;
        _warming = false;
        if (_scale != scale || _scaler.Scale != scale)
        {
            _scale = scale;
            _scaler.Scale = scale;
        }
        if (!IsVisible) Show();
        Reposition();
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(200, durationMs));
        _hideTimer.Start();
    }

    private void Reposition()
    {
        if (_warming) return;
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
