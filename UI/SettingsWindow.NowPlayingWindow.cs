using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double PlayerWindowWidth = 1280, PlayerWindowHeight = 800;
        private const double ResizeGrip = 6;
        private bool _npBorderless;
        private (Button Button, PathIcon Icon) _npBorderlessToggle;
        private Panel? _npResizeGrips;
        private Window? _npWindow;
        private ScaledContent? _npWindowScaler;
        private Panel? _npOverlayHome;
        private bool _npQuietClose;
        private PixelRect? _npWindowBounds;
        private (Button Button, PathIcon Icon) _npPopOut;
        private Button? _npCloseButton;

        private Window NowPlayingHost => _npWindow ?? this;

        private bool PlayerWindowShowing => _npWindow is { IsVisible: true } window && window.WindowState != WindowState.Minimized;

        private Button BuildPopOutButton()
        {
            _npPopOut = IconPill(Icons.OpenInWindow, null);
            _npPopOut.Button.Click += (_, _) =>
            {
                if (_npWindow == null) OpenNowPlayingWindow();
                else PutNowPlayingBack();
            };
            ApplyPopOutButton();
            return _npPopOut.Button;
        }

        private Button BuildBorderlessButton()
        {
            _npBorderlessToggle = IconPill(Icons.Borderless, null);
            _npBorderlessToggle.Button.Click += (_, _) => SetNowPlayingBorderless(!_npBorderless);
            _npTopBar.PointerPressed += (_, e) =>
            {
                if (_npWindow is not { } window || !_npBorderless || _npRestoreState != null) return;
                if (!e.GetCurrentPoint(_npTopBar).Properties.IsLeftButtonPressed) return;
                if (e.Source is Visual source && (source as Button ?? source.FindAncestorOfType<Button>()) != null) return;
                window.BeginMoveDrag(e);
            };
            ApplyBorderlessButton();
            return _npBorderlessToggle.Button;
        }

        private void ApplyBorderlessButton()
        {
            if (_npBorderlessToggle.Button == null) return;
            _npBorderlessToggle.Button.IsVisible = _npWindow != null;
            _npBorderlessToggle.Icon.Data = _npBorderless ? Icons.WindowFrame : Icons.Borderless;
            ToolTip.SetTip(_npBorderlessToggle.Button, _npBorderless ? "Show the window frame" : "Borderless window: move it by dragging the top bar, resize it at the edges");
        }

        private void SetNowPlayingBorderless(bool on)
        {
            _npBorderless = on;
            if (_npWindow is { } window) window.SystemDecorations = on ? SystemDecorations.None : SystemDecorations.Full;
            if (_npResizeGrips != null) _npResizeGrips.IsVisible = on;
            ApplyBorderlessButton();
            WakeNowPlaying();
        }

        private Panel BuildResizeGrips(Window window)
        {
            var grips = new Panel { IsVisible = _npBorderless };
            void Grip(WindowEdge edge, StandardCursorType cursor, HorizontalAlignment across, VerticalAlignment down, double width, double height)
            {
                var grip = new Border
                {
                    Background = Brushes.Transparent, HorizontalAlignment = across, VerticalAlignment = down, Cursor = new Cursor(cursor)
                };
                if (!double.IsNaN(width)) grip.Width = width;
                if (!double.IsNaN(height)) grip.Height = height;
                grip.PointerPressed += (_, e) =>
                {
                    if (_npRestoreState != null || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return;
                    e.Handled = true;
                    window.BeginResizeDrag(edge, e);
                };
                grips.Children.Add(grip);
            }
            const double any = double.NaN, corner = ResizeGrip * 2;
            Grip(WindowEdge.West, StandardCursorType.LeftSide, HorizontalAlignment.Left, VerticalAlignment.Stretch, ResizeGrip, any);
            Grip(WindowEdge.East, StandardCursorType.RightSide, HorizontalAlignment.Right, VerticalAlignment.Stretch, ResizeGrip, any);
            Grip(WindowEdge.North, StandardCursorType.TopSide, HorizontalAlignment.Stretch, VerticalAlignment.Top, any, ResizeGrip);
            Grip(WindowEdge.South, StandardCursorType.BottomSide, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, any, ResizeGrip);
            Grip(WindowEdge.NorthWest, StandardCursorType.TopLeftCorner, HorizontalAlignment.Left, VerticalAlignment.Top, corner, corner);
            Grip(WindowEdge.NorthEast, StandardCursorType.TopRightCorner, HorizontalAlignment.Right, VerticalAlignment.Top, corner, corner);
            Grip(WindowEdge.SouthWest, StandardCursorType.BottomLeftCorner, HorizontalAlignment.Left, VerticalAlignment.Bottom, corner, corner);
            Grip(WindowEdge.SouthEast, StandardCursorType.BottomRightCorner, HorizontalAlignment.Right, VerticalAlignment.Bottom, corner, corner);
            return grips;
        }

        private void ApplyPopOutButton()
        {
            if (_npPopOut.Button == null) return;
            bool popped = _npWindow != null;
            _npPopOut.Icon.Data = popped ? Icons.PutBackWindow : Icons.OpenInWindow;
            ToolTip.SetTip(_npPopOut.Button, popped ? "Put back in the main window" : "Open in its own window");
            ApplyBorderlessButton();
            if (_npCloseButton != null) ToolTip.SetTip(_npCloseButton, popped ? "Close the player (Esc)" : "Back to Wiim Control (Esc)");
        }

        private void OpenNowPlayingWindow()
        {
            if (_npWindow != null || _npOverlay.Parent is not Panel home) return;
            SetNowPlayingFullScreen(false);
            _npOverlayHome = home;
            home.Children.Remove(_npOverlay);
            _npWindowScaler = new ScaledContent { Scale = _scale, Child = _npOverlay };
            var window = new Window
            {
                Title = "Wiim Control", Icon = Icon, Background = new SolidColorBrush(Color.Parse("#0E0E0E")),
                Width = PlayerWindowWidth, Height = PlayerWindowHeight, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                SystemDecorations = _npBorderless ? SystemDecorations.None : SystemDecorations.Full
            };
            _npResizeGrips = BuildResizeGrips(window);
            window.Content = new Panel { Children = { _npWindowScaler, _npResizeGrips } };
            if (_npWindowBounds is { } bounds)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = bounds.Position;
                window.Width = bounds.Width;
                window.Height = bounds.Height;
            }
            window.KeyDown += (_, e) =>
            {
                if (e.Key == Key.F11) SetNowPlayingFullScreen(_npRestoreState == null);
                if (e.Key != Key.Escape) return;
                if (_npRestoreState != null) SetNowPlayingFullScreen(false);
                else HideNowPlayingView();
            };
            window.PropertyChanged += (_, e) =>
            {
                if (e.Property != WindowStateProperty || window.WindowState == WindowState.FullScreen || _npRestoreState == null) return;
                _npRestoreState = null;
                ApplyFullScreenButton();
            };
            window.Closing += (_, _) =>
            {
                if (window.WindowState == WindowState.Normal)
                    _npWindowBounds = new PixelRect(window.Position, new PixelSize((int)window.Width, (int)window.Height));
            };
            window.Closed += (_, _) =>
            {
                bool quiet = _npQuietClose;
                _npQuietClose = false;
                DockNowPlaying();
                if (!quiet) HideNowPlayingView();
                if (!IsVisible && _npIp == null) Close();
            };
            _npWindow = window;
            ApplyPopOutButton();
            window.Show();
            WakeNowPlaying();
        }

        private void ShowNowPlayingWindow(string ip)
        {
            ShowNowPlayingView(ip);
            OpenNowPlayingWindow();
        }

        private void PutNowPlayingBack()
        {
            if (_npWindow is not { } window) return;
            _npQuietClose = true;
            window.Close();
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            WakeNowPlaying();
        }

        private void CloseNowPlayingWindow()
        {
            if (_npWindow is not { } window) return;
            _npQuietClose = true;
            window.Close();
        }

        private void DockNowPlaying()
        {
            if (_npWindowScaler != null) _npWindowScaler.Child = null;
            _npOverlay.ClearValue(RenderTransformProperty);
            _npOverlay.ClearValue(RenderTransformOriginProperty);
            if (_npOverlay.Parent == null && _npOverlayHome != null) _npOverlayHome.Children.Add(_npOverlay);
            _npWindow = null;
            _npWindowScaler = null;
            _npResizeGrips = null;
            _npRestoreState = null;
            ApplyFullScreenButton();
            ApplyPopOutButton();
        }
    }
}
