using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace WiimControl;

sealed class TrackToastWindow : Window
{
    private const double ToastWidth = 340;
    private static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(5);

    private readonly Image _art = new() { Stretch = Stretch.UniformToFill, IsVisible = false };
    private readonly PathIcon _placeholder = new()
    {
        Data = Icons.Music, Width = 26, Height = 26, Foreground = Palette.TextMuted,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _title = new()
    {
        FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly TextBlock _artist = new()
    {
        FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(214, 214, 214)), TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, 2, 0, 0)
    };
    private readonly TextBlock _footer = new()
    {
        FontSize = 11.5, Foreground = Palette.TextMuted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 0)
    };
    private readonly DispatcherTimer _hideTimer = new() { Interval = ShowFor };
    private readonly ScaledContent _scaler = new();
    private OsdCorner _corner;
    private Action? _onClick;

    public event Action? VisibilityChanged;

    public TrackToastWindow()
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        var artBox = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(8), ClipToBounds = true, Background = Palette.Input,
            Child = new Panel { Children = { _placeholder, _art } }
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Children = { _title, _artist, _footer } };
        var row = new DockPanel { Children = { artBox, text } };
        DockPanel.SetDock(artBox, Dock.Left);
        _scaler.Child = new Border
        {
            Width = ToastWidth, Padding = new Thickness(12), CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)), BorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
            BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = row
        };
        Content = _scaler;
        ToolTip.SetTip(_scaler.Child, "Open the mini player");

        _hideTimer.Tick += (_, _) => HideToast();
        Opened += (_, _) => { WindowChrome.MakeToolWindow(this); Reposition(); };
        SizeChanged += (_, _) =>
        {
            Reposition();
            if (IsVisible) VisibilityChanged?.Invoke();
        };
        PointerEntered += (_, _) => _hideTimer.Stop();
        PointerExited += (_, _) => RestartTimer();
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var onClick = _onClick;
            HideToast();
            onClick?.Invoke();
        };
    }

    public bool IsShowingArtwork(Bitmap bitmap) => ReferenceEquals(_art.Source, bitmap);

    public void ShowTrack(Bitmap? art, string title, string artist, string footer, OsdCorner corner, double scale, Action onClick)
    {
        _art.Source = art;
        _art.IsVisible = art != null;
        _placeholder.IsVisible = art == null;
        _title.Text = title;
        _artist.Text = artist;
        _artist.IsVisible = artist.Length > 0;
        _footer.Text = footer;
        _footer.IsVisible = footer.Length > 0;
        _corner = corner;
        _scaler.Scale = scale;
        _onClick = onClick;
        bool wasVisible = IsVisible;
        if (!IsVisible) Show();
        Reposition();
        RestartTimer();
        if (!wasVisible) VisibilityChanged?.Invoke();
    }

    private void RestartTimer()
    {
        if (!IsVisible) return;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HideToast()
    {
        _hideTimer.Stop();
        _onClick = null;
        Hide();
        _art.Source = null;
        VisibilityChanged?.Invoke();
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
