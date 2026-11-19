using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private async Task HoldArtDuringLookupAsync(ArtworkView art)
        {
            int hold = _npArtHold;
            while (_owner.OnlineArtworkSearching)
            {
                await Task.Delay(ArtLookupPoll);
                if (hold != _npArtHold) return;
            }
            if (hold == _npArtHold && _npControls?.Art == art && art.Bitmap == null) ShowNowPlayingArt(null, flip: true);
        }

        private async Task ShowSharpArtworkAsync(ArtworkView art, string url, Bitmap thumbnail, bool fade)
        {
            var largeTask = _owner.GetLargeArtworkAsync(url);
            if (largeTask.IsCompletedSuccessfully && largeTask.Result is { } cached)
            {
                ShowNowPlayingArt(cached, fade, flip: true);
                return;
            }
            if (!fade) ShowNowPlayingArt(null, fade: false);
            bool arrived = await Task.WhenAny(largeTask, Task.Delay(LargeArtworkWait)) == largeTask;
            bool Current() => _npControls?.Art == art && art.ImageUrl == url;
            if (!Current()) return;
            if (!arrived) ShowNowPlayingArt(thumbnail, flip: true);
            var large = await largeTask;
            if (!Current()) return;
            if (large != null) ShowNowPlayingArt(large, flip: arrived);
            else if (arrived) ShowNowPlayingArt(thumbnail, flip: true);
        }

        private void ShowNowPlayingArt(Bitmap? bitmap, bool fade = true, bool flip = false)
        {
            if (!fade)
            {
                _npPendingArt = null;
                _npHasPendingArt = false;
            }
            var previous = _npCover.IsVisible ? _npCover.Source : null;
            if (ReferenceEquals(previous, bitmap) && bitmap != null) return;
            if (fade && MascotBusy && NpStyle == NowPlayingStyle.Mascot)
            {
                ReplaceIncomingArt(bitmap);
                return;
            }
            if (fade && (_npFlip || _npSwap) && _npFadeTimer.IsEnabled)
            {
                if (_npHasPendingArt) _npPendingArt = bitmap;
                else if (!flip || !PastArtSwitch()) ReplaceIncomingArt(bitmap);
                else
                {
                    _npPendingArt = bitmap;
                    _npHasPendingArt = true;
                }
                return;
            }
            _npCoverOld.Source = previous;
            ReleaseNowPlayingBackdrop();
            _npBackdropOld.Source = _npBackdrop.Source;
            foreach (var (art, old) in _npArtShapes) old.Fill = art.Fill;
            _npCover.Source = bitmap;
            _npBackdrop.Source = bitmap != null ? WiimController.BlurForNowPlaying(bitmap) : null;
            _npCover.IsVisible = bitmap != null;
            _npCoverPlaceholder.IsVisible = bitmap == null;
            IBrush artFill = NowPlayingArtFill(bitmap);
            foreach (var (art, _) in _npArtShapes) art.Fill = artFill;
            _npFadeFromEmpty = previous == null;
            var style = NpStyle;
            _npFlip = flip && previous != null && (style is NowPlayingStyle.Cover or NowPlayingStyle.BigVinyl or NowPlayingStyle.Gatefold || IsVuStyle(style) || IsAnalyzer(style) || IsAmbient(style));
            _npSwap = flip && previous != null && style is NowPlayingStyle.Vinyl or NowPlayingStyle.Cassette or NowPlayingStyle.Cd;
            _npFadeStart = DateTime.UtcNow;
            if (style == NowPlayingStyle.Mascot && flip && fade && previous != null && _npOverlay.IsVisible)
            {
                StartMascotSwap();
                return;
            }
            if (fade && _npOverlay.IsVisible && (previous != null || bitmap != null))
            {
                ApplyArtFade(0);
                _npFadeTimer.Start();
                if (_npSwap || _npFlip) UpdateStageMotion();
            }
            else EndArtFade();
        }

        private bool PastArtSwitch()
        {
            double seconds = _npSwap ? ArtSwapSeconds : ArtFlipSeconds;
            double progress = (DateTime.UtcNow - _npFadeStart).TotalSeconds / seconds;
            return progress >= (_npSwap ? SwapInEnd + (SwapFlipEnd - SwapInEnd) / 2 : 0.5);
        }

        private void ReplaceIncomingArt(Bitmap? bitmap)
        {
            if (_npBackdrop.Source is Bitmap blurred) _owner.DisposeWhenUnused([blurred]);
            _npCover.Source = bitmap;
            _npBackdrop.Source = bitmap != null ? WiimController.BlurForNowPlaying(bitmap) : null;
            _npCover.IsVisible = bitmap != null;
            _npCoverPlaceholder.IsVisible = bitmap == null;
            IBrush artFill = NowPlayingArtFill(bitmap);
            foreach (var (art, _) in _npArtShapes) art.Fill = artFill;
        }

        private IBrush NowPlayingArtFill(Bitmap? bitmap)
        {
            if (bitmap != null) return new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            const double size = 100, iconSize = 34;
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing { Brush = Palette.Input, Geometry = new RectangleGeometry(new Rect(0, 0, size, size)) });
            if (_npCoverPlaceholder.Data is { } icon && icon.Bounds is { Width: > 0, Height: > 0 } bounds)
            {
                double scale = iconSize / Math.Max(bounds.Width, bounds.Height);
                var place = Matrix.CreateTranslation(-bounds.X - bounds.Width / 2, -bounds.Y - bounds.Height / 2) *
                    Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(size / 2, size / 2);
                drawing.Children.Add(new DrawingGroup
                {
                    Transform = new MatrixTransform(place),
                    Children = { new GeometryDrawing { Brush = Palette.TextDim, Geometry = icon } }
                });
            }
            return new DrawingBrush(drawing) { Stretch = Stretch.UniformToFill };
        }

        private void SetNowPlayingPlaceholder(StreamGeometry icon)
        {
            if (ReferenceEquals(_npCoverPlaceholder.Data, icon)) return;
            _npCoverPlaceholder.Data = icon;
            if (_npCover.Source != null) return;
            var fill = NowPlayingArtFill(null);
            foreach (var (art, _) in _npArtShapes) art.Fill = fill;
        }

        private void StepArtFade()
        {
            double seconds = _npSwap ? ArtSwapSeconds : _npFlip ? ArtFlipSeconds : ArtFadeSeconds;
            double progress = (DateTime.UtcNow - _npFadeStart).TotalSeconds / seconds;
            if (progress >= 1) EndArtFade();
            else ApplyArtFade(progress);
        }

        private static double Ease(double progress) => progress * progress * (3 - 2 * progress);

        private static double Stage(double progress, double start, double end) => Math.Clamp((progress - start) / (end - start), 0, 1);

        private void ApplySwap(double progress)
        {
            double slide = progress < SwapInEnd ? Ease(Stage(progress, 0, SwapInEnd)) : 1 - Ease(Stage(progress, SwapFlipEnd, 1));
            double inside = progress < SwapInEnd ? Stage(progress, 0, SwapInEnd) : 1 - Stage(progress, SwapFlipEnd, 1);
            double hidden = 1 - Stage(inside, 0.8, 1);
            _npDiscSlide.X = _npSheenSlide.X = slide * ((StageCover - DiscSize) / 2 - DiscLeft);
            _npCassetteSlide.X = slide * ((StageCover - CassetteWidth) / 2 - CassetteLeft);
            _npCdSlide.X = slide * ((StageCover - CdSize) / 2 - CdLeft);
            _npDisc.Opacity = _npSheen.Opacity = _npCassette.Opacity = _npCd.Opacity = hidden;
            double turn = Ease(Stage(progress, SwapInEnd, SwapFlipEnd));
            bool front = turn < 0.5;
            _npCoverFlip.AngleY = _npCaseFlip.AngleY = front ? turn * 180 : turn * 180 - 180;
            _npCoverOld.Opacity = front ? 1 : 0;
            foreach (var (_, faded) in _npArtShapes) faded.Opacity = front ? 1 : 0;
            BlendBackdrops(1 - turn);
            _npCover.Opacity = 1;
        }

        private void ApplyArtFade(double progress)
        {
            double eased = Ease(progress);
            double old = _npFadeFromEmpty ? 0 : 1 - eased;
            if (_npSwap)
            {
                ApplySwap(progress);
                return;
            }
            if (_npFlip)
            {
                double angle = eased * 180;
                bool front = angle < 90;
                _npCoverFlip.AngleY = _npAmbientFlip.AngleY = _npBigDiscFlip.AngleY = front ? angle : angle - 180;
                _npCoverOld.Opacity = front ? 1 : 0;
                foreach (var (_, faded) in _npArtShapes) faded.Opacity = _npCoverOld.Opacity;
                BlendBackdrops(old);
                _npCover.Opacity = 1;
                return;
            }
            _npCoverOld.Opacity = old;
            foreach (var (_, faded) in _npArtShapes) faded.Opacity = old;
            BlendBackdrops(old);
            _npCover.Opacity = _npFadeFromEmpty ? eased : 1;
        }

        private void EndArtFade()
        {
            _npFadeTimer.Stop();
            bool swapped = _npSwap || _npFlip;
            _npFlip = _npSwap = false;
            _npCoverFlip.AngleY = _npAmbientFlip.AngleY = _npBigDiscFlip.AngleY = _npCaseFlip.AngleY = 0;
            _npDiscSlide.X = _npSheenSlide.X = _npCassetteSlide.X = _npCdSlide.X = 0;
            _npDisc.Opacity = _npSheen.Opacity = _npCassette.Opacity = _npCd.Opacity = 1;
            if (swapped) UpdateStageMotion();
            _npCoverOld.Opacity = 0;
            BlendBackdrops(0);
            _npCover.Opacity = 1;
            _npCoverOld.Source = null;
            ReleaseNowPlayingBackdrop();
            foreach (var (_, faded) in _npArtShapes)
            {
                faded.Opacity = 0;
                faded.Fill = null;
            }
            if (!_npHasPendingArt) return;
            var next = _npPendingArt;
            _npPendingArt = null;
            _npHasPendingArt = false;
            if (_npOverlay.IsVisible) ShowNowPlayingArt(next, flip: true);
        }

        private void BlendBackdrops(double old)
        {
            double level = _npBackdropLevel;
            _npBackdropOld.Opacity = level * old;
            _npBackdrop.Opacity = old >= 1 ? 0 : level * (1 - old) / (1 - level * old);
        }

        private void ReleaseNowPlayingBackdrop()
        {
            if (_npBackdropOld.Source is not Bitmap old) return;
            _npBackdropOld.Source = null;
            _owner.DisposeWhenUnused([old]);
        }
    }
}
