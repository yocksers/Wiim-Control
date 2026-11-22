using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private static string StyleName(NowPlayingStyle style) => style switch
        {
            NowPlayingStyle.Vinyl => "Vinyl record",
            NowPlayingStyle.BigVinyl => "Big vinyl",
            NowPlayingStyle.Cassette => "Cassette tape",
            NowPlayingStyle.Cd => "CD",
            NowPlayingStyle.Reel => "Reel-to-reel",
            NowPlayingStyle.Ambient => "Ambient",
            NowPlayingStyle.Lyrics => "Lyrics only",
            NowPlayingStyle.Gatefold => "Gatefold with track list",
            NowPlayingStyle.Mascot => "Mascot",
            NowPlayingStyle.VuMeters => "Stereo VU meters",
            NowPlayingStyle.VuMeter => "VU meter",
            NowPlayingStyle.Analyzer => "Spectrum",
            NowPlayingStyle.StereoAnalyzer => "Stereo Spectrum",
            NowPlayingStyle.LedMatrix => "Dot Matrix",
            NowPlayingStyle.Fire => "Fire",
            _ => "Cover only"
        };

        private static bool IsAmbient(NowPlayingStyle style) => style == NowPlayingStyle.Ambient;

        private void ApplyNowPlayingStyle()
        {
            var style = NpStyle;
            _npAppliedStyle = style;
            bool ambient = IsAmbient(style);
            _npDisc.IsVisible = _npSheen.IsVisible = style == NowPlayingStyle.Vinyl;
            _npBigDisc.IsVisible = _npBigControls.IsVisible = style == NowPlayingStyle.BigVinyl;
            _npCassette.IsVisible = style == NowPlayingStyle.Cassette;
            _npCd.IsVisible = _npCase.IsVisible = style == NowPlayingStyle.Cd;
            _npBigReel.IsVisible = style == NowPlayingStyle.Reel;
            _npGatefold.IsVisible = style == NowPlayingStyle.Gatefold;
            bool lyricsOnly = style == NowPlayingStyle.Lyrics;
            _npAmbient.IsVisible = ambient;
            _npStageBox.IsVisible = !ambient && !lyricsOnly;
            _npCoverCard.IsVisible = !ambient && style is not (NowPlayingStyle.BigVinyl or NowPlayingStyle.Reel);
            (_npStage.Width, _npStage.Height) = style switch
            {
                NowPlayingStyle.Vinyl => (DiscLeft + DiscSize, StageCover),
                NowPlayingStyle.BigVinyl => (BigDiscSize, BigDiscSize),
                NowPlayingStyle.Cassette => (CassetteLeft + CassetteWidth, StageCover),
                NowPlayingStyle.Cd => (CdLeft + CdSize, StageCover),
                NowPlayingStyle.Reel => (BigReelWidth, BigReelHeight),
                NowPlayingStyle.Gatefold => (StageCover * 2, StageCover),
                NowPlayingStyle.Mascot => (MascotStageWidth, StageCover),
                NowPlayingStyle.VuMeters => (VuStageWidth, VuStageHeight),
                NowPlayingStyle.VuMeter => (AnalyzerStageWidth, StageCover),
                NowPlayingStyle.Analyzer or NowPlayingStyle.LedMatrix or NowPlayingStyle.Fire => (AnalyzerStageWidth, StageCover),
                NowPlayingStyle.StereoAnalyzer => (StereoStageWidth, StereoStageHeight),
                _ => (StageCover, StageCover)
            };
            _npStyleButton.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children =
                {
                    new TextBlock { Text = StyleName(style), VerticalAlignment = VerticalAlignment.Center },
                    new PathIcon { Data = Icons.ChevronDown, Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center }
                }
            };
            ArrangeNowPlayingParts();
            _npBackdropLevel = _owner.NowPlayingBrightness(style) / 100.0;
            BlendBackdrops(0);
            PlaceNowPlayingControls();
            ApplyLyricsVisibility();
            UpdateStageMotion();
            if (style == NowPlayingStyle.Gatefold) OpenGatefold();
            else _npGatefoldTimer.Stop();
            ApplyMascotStyle(style == NowPlayingStyle.Mascot);
            ApplyVuStyle(style);
            ApplyAnalyzerStyle(style);
            ApplyStereoAnalyzerStyle(style == NowPlayingStyle.StereoAnalyzer);
            ApplyNowPlayingControls();
            if (_npOverlay.IsVisible) WakeNowPlaying();
        }

        private static Avalonia.Animation.Transitions FadeTransitions() =>
        [
            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(400) }
        ];

        private void WakeNowPlaying()
        {
            _npTopBar.Opacity = _npAmbientControls.Opacity = 1;
            _npOverlay.Cursor = null;
            _npIdleTimer.Stop();
            _npIdleTimer.Start();
        }

        private void HideNowPlayingControls()
        {
            bool ambient = NpStyle == NowPlayingStyle.Ambient;
            if (_npTopBar.IsPointerOver || _npBrightnessFlyout.IsOpen || _npLyricsDelayFlyout.IsOpen || _npVuSensitivityFlyout.IsOpen || ambient && _npAmbientControls.IsPointerOver) return;
            _npIdleTimer.Stop();
            _npTopBar.Opacity = 0;
            if (ambient) _npAmbientControls.Opacity = 0;
            if (ambient || NpStyle == NowPlayingStyle.Lyrics) _npOverlay.Cursor = new Cursor(StandardCursorType.None);
        }

        private void PlaceNowPlayingControls()
        {
            if (_npControlPanel is not { } panel || _npControlCard is not { } card) return;
            var style = NpStyle;
            bool under = _owner.NowPlayingControlsUnder && CanPlaceControlsUnder(style);
            Decorator host = style == NowPlayingStyle.Ambient ? _npAmbientControls : card;
            UpdateNowPlayingCards();
            _npUnder.Margin = new Thickness(ShadowRoom.Left + (style == NowPlayingStyle.Mascot ? MascotCoverLeft : 0), UnderControlsGap - ShadowRoom.Bottom,
                ShadowRoom.Right, ShadowRoom.Bottom);
            if (panel.Parent == host) return;
            if (panel.Parent is Decorator previous) previous.Child = null;
            host.Child = panel;
        }

        private void UpdateStageMotion()
        {
            bool playing = _npOverlay.IsVisible && _npControls?.Playing.IsVisible == true;
            var style = NpStyle;
            _npLabelSpin.Run(playing && style == NowPlayingStyle.Vinyl && !_npSwap);
            _npBigLabelSpin.Run(playing && style == NowPlayingStyle.BigVinyl && !_npFlip);
            _npCassetteReels?.Run(playing && style == NowPlayingStyle.Cassette && !_npSwap);
            _npCdSpin.Run(playing && style == NowPlayingStyle.Cd && !_npSwap);
            UpdateAmbientDrift(_npOverlay.IsVisible && IsAmbient(style));
            _npBigReelReels?.Run(playing && style == NowPlayingStyle.Reel);
            UpdateMascotTimer();
            UpdateVuMeters();
            UpdateAnalyzer();
        }

        private void UpdateTapePacks()
        {
            double progress = _npControls is { PositionRow.IsVisible: true } c ? (double)c.Position.Value / Math.Max(1, c.Position.Maximum) : 0.5;
            _npCassetteReels?.Wind(progress);
            _npBigReelReels?.Wind(progress);
        }
    }
}
