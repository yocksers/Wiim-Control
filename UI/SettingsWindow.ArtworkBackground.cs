using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private static readonly IBrush SidebarOverArtwork = Palette.SidebarGlass;

        private readonly Image _backdropA = BackdropImage();
        private readonly Image _backdropB = BackdropImage();
        private readonly Border _backdropScrim = new() { Background = Palette.WindowScrim, IsHitTestVisible = false };
        private Border? _sidebar;
        private Button? _showMenuButton;
        private bool _backdropOnA;
        private string _backdropUrl = string.Empty;

        private static Image BackdropImage()
        {
            var image = new Image
            {
                Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false,
                Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(700) } }
            };
            RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
            return image;
        }

        internal void ApplyArtworkBackground() => _ = ShowArtworkBackgroundAsync(_owner.BackgroundArtUrl);

        private bool IsBackdrop(Bitmap bitmap) =>
            ReferenceEquals(_backdropA.Source, bitmap) || ReferenceEquals(_backdropB.Source, bitmap);

        private async Task ShowArtworkBackgroundAsync(string url)
        {
            if (url == _backdropUrl) return;
            _backdropUrl = url;
            var bitmap = url.Length > 0 ? await _owner.GetBackdropAsync(url) : null;
            if (url != _backdropUrl) return;
            var outgoing = _backdropOnA ? _backdropA : _backdropB;
            if (bitmap != null)
            {
                var incoming = _backdropOnA ? _backdropB : _backdropA;
                incoming.Source = bitmap;
                incoming.Opacity = 1;
                _backdropOnA = !_backdropOnA;
            }
            outgoing.Opacity = 0;
            Classes.Set("artwork", bitmap != null);
            if (_sidebar != null) _sidebar.Background = bitmap != null ? SidebarOverArtwork : Palette.Sidebar;
        }
    }
}
