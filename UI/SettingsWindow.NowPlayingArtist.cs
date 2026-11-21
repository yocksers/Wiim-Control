using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double ArtistThumbGap = 26;

        private readonly Panel _npArtistPhotoLayer = new() { IsHitTestVisible = false, IsVisible = false };
        private readonly Image[] _npArtistPhotos =
        [
            new() { Stretch = Stretch.UniformToFill, Opacity = 0, Transitions = ArtistPhotoFade() },
            new() { Stretch = Stretch.UniformToFill, Opacity = 0, Transitions = ArtistPhotoFade() }
        ];
        private readonly Grid _npArtistUnder = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
        private readonly Border _npArtistThumb = new()
        {
            CornerRadius = new CornerRadius(8), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, ArtistThumbGap, 0), BoxShadow = BoxShadows.Parse("0 10 30 0 #A0000000")
        };
        private readonly Border _npArtistControls = new() { Width = StageCover, VerticalAlignment = VerticalAlignment.Top };
        private int _npArtistFront;
        private int _npArtistVersion;
        private string? _npArtistPhotoName;

        private static Avalonia.Animation.Transitions ArtistPhotoFade() =>
        [
            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(700) }
        ];

        private void BuildArtistPhoto()
        {
            foreach (var photo in _npArtistPhotos) _npArtistPhotoLayer.Children.Add(photo);
            var art = new Rectangle();
            var old = new Rectangle { Opacity = 0, IsHitTestVisible = false };
            _npArtShapes.Add((art, old));
            _npArtistThumb.Child = new Panel { Children = { art, old } };
            _npArtistUnder.Children.Add(_npArtistThumb);
            Grid.SetColumn(_npArtistControls, 1);
            _npArtistUnder.Children.Add(_npArtistControls);
            _npArtistControls.SizeChanged += (_, e) => FitArtistThumb(e.NewSize.Height);
        }

        private void FitArtistThumb(double height)
        {
            if (height <= 0) return;
            _npArtistThumb.Width = _npArtistThumb.Height = height;
            double reach = height + ArtistThumbGap;
            _npArtistUnder.Margin = new Thickness(ShadowRoom.Left - reach, UnderControlsGap - ShadowRoom.Bottom, ShadowRoom.Right, ShadowRoom.Bottom);
            if (_npArtistUnder.Parent is StackPanel column) column.Margin = new Thickness(_npArtistUnder.IsVisible ? Math.Max(0, reach - ShadowRoom.Left) : 0, 0, 0, 0);
        }

        private bool ArtistPhotoShown => _npArtistPhotos.Any(p => p.Opacity > 0 && p.Source != null);

        private bool IsArtistPhoto(Bitmap bitmap) => _npArtistPhotos.Any(p => ReferenceEquals(p.Source, bitmap));

        private void ApplyArtistStyle(bool on)
        {
            _npArtistPhotoLayer.IsVisible = on;
            _npArtistPhotoName = null;
            if (on) UpdateArtistPhoto();
        }

        private void PlaceArtistUnder(Control card, bool on)
        {
            card.IsVisible = !on;
            _npArtistUnder.IsVisible = on;
            FitArtistThumb(_npArtistControls.Bounds.Height);
            if (!on && _npArtistUnder.Parent is StackPanel column) column.Margin = default;
        }

        private void DetachArtistRow() => _npArtistControls.Child = null;

        private void WatchArtistLine(TextBlock artist) => artist.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.TextProperty) UpdateArtistPhoto();
        };

        private void PrefetchArtistPhoto(string artistLine)
        {
            if (NpStyle != NowPlayingStyle.Artist || !_npOverlay.IsVisible) return;
            _ = _owner.GetArtistPhotoAsync(ArtistPhotoName(artistLine));
        }

        private async void UpdateArtistPhoto()
        {
            if (NpStyle != NowPlayingStyle.Artist || _npControls is not { } controls) return;
            if (!_owner.OnlineArtwork)
            {
                _npArtistPhotoName = null;
                ShowArtistPhoto(null);
                return;
            }
            string name = ArtistPhotoName(controls.Artist.Text ?? string.Empty);
            if (name == _npArtistPhotoName) return;
            _npArtistPhotoName = name;
            int version = ++_npArtistVersion;
            var photo = await _owner.GetArtistPhotoAsync(name);
            if (version != _npArtistVersion || NpStyle != NowPlayingStyle.Artist) return;
            ShowArtistPhoto(photo);
        }

        private void ShowArtistPhoto(Bitmap? photo)
        {
            var front = _npArtistPhotos[_npArtistFront];
            if (photo != null && ReferenceEquals(front.Source, photo) && front.Opacity > 0) return;
            if (photo == null)
            {
                foreach (var image in _npArtistPhotos) image.Opacity = 0;
                return;
            }
            _npArtistFront = 1 - _npArtistFront;
            var next = _npArtistPhotos[_npArtistFront];
            next.Source = photo;
            next.ZIndex = 1;
            front.ZIndex = 0;
            next.Opacity = 1;
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                if (!ReferenceEquals(_npArtistPhotos[_npArtistFront], front)) front.Opacity = 0;
            }, TimeSpan.FromMilliseconds(750));
        }
    }
}
