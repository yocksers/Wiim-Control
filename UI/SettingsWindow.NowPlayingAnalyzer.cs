using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double AnalyzerWidth = 900;
        private const double AnalyzerGap = 40;
        private const double AnalyzerStageWidth = StageCover + AnalyzerGap + AnalyzerWidth;
        private const double StereoBoxWidth = 760;
        private const double StereoBoxHeight = 460;
        private const double StereoGap = 40;
        private const double StereoStageWidth = StereoBoxWidth * 2 + StereoGap;
        private const double StereoCoverSize = 240;
        private const double StereoInfoWidth = 640;
        private const double StereoInfoGap = 32;
        private const double StereoBoxesTop = StereoCoverSize + StereoGap;
        private const double StereoStageHeight = StereoBoxesTop + StereoBoxHeight;
        private const double StereoRowLeft = (StereoStageWidth - StereoCoverSize - StereoInfoGap - StereoInfoWidth) / 2;

        private readonly SpectrumAnalyzer _npAnalyzer = new() { Width = AnalyzerWidth, Height = StageCover };
        private readonly LedMatrixVisual _npLedMatrix = new() { Width = AnalyzerWidth, Height = StageCover };
        private readonly FireVisual _npFire = new() { Width = AnalyzerWidth, Height = StageCover };
        private static readonly BoxShadows AnalyzerShadow = BoxShadows.Parse("0 12 48 0 #E0000000");
        private readonly Border _npAnalyzerBox = new() { CornerRadius = new CornerRadius(6), IsVisible = false };
        private readonly (Button Button, PathIcon Icon) _npAnalyzerBoxToggle = IconButton(Icons.Square);
        private readonly SpectrumAnalyzer _npStereoLeft = new() { Width = StereoBoxWidth, Height = StereoBoxHeight, Label = "L" };
        private readonly SpectrumAnalyzer _npStereoRight = new() { Width = StereoBoxWidth, Height = StereoBoxHeight, Label = "R" };
        private readonly Border _npStereoLeftBox = new() { CornerRadius = new CornerRadius(6) };
        private readonly Border _npStereoRightBox = new() { CornerRadius = new CornerRadius(6) };
        private readonly Canvas _npStereoPanel = new() { Width = StereoStageWidth, Height = StereoStageHeight, IsVisible = false };
        private readonly StackPanel _npStereoInfo = new() { Width = StereoInfoWidth, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        private readonly FrameTimer _npAnalyzerTimer = new();
        private readonly float[] _npAnalyzerSamples = new float[SpectrumAnalyzer.SampleCount];
        private IAudioSamples? _npAnalyzerSource;
        private string? _npAnalyzerSourceId;
        private DateTime _npAnalyzerLast;

        private static bool UsesComputerSound(NowPlayingStyle style) =>
            style is NowPlayingStyle.VuMeters or NowPlayingStyle.VuMeter || IsAnalyzer(style);

        private static bool IsAnalyzer(NowPlayingStyle style) =>
            style is NowPlayingStyle.Analyzer or NowPlayingStyle.StereoAnalyzer or NowPlayingStyle.LedMatrix or NowPlayingStyle.Fire;

        private static bool UsesAnalyzerBox(NowPlayingStyle style) => style is NowPlayingStyle.Analyzer or NowPlayingStyle.LedMatrix or NowPlayingStyle.Fire;

        private void BuildAnalyzer()
        {
            var toggle = _npAnalyzerBoxToggle.Button;
            _npAnalyzerBoxToggle.Icon.Width = _npAnalyzerBoxToggle.Icon.Height = 20;
            ToolTip.SetTip(toggle, "Show or hide the dark background behind the bars");
            toggle.Click += (_, _) =>
            {
                _owner.AnalyzerBackground = !_owner.AnalyzerBackground;
                ShowAnalyzerBackground();
            };
            _npAnalyzerBox.Child = _npAnalyzer;
            _npStereoLeftBox.Child = _npStereoLeft;
            _npStereoRightBox.Child = _npStereoRight;
            _npStereoPanel.Children.Add(Place(_npStereoLeftBox, 0, StereoBoxesTop));
            _npStereoPanel.Children.Add(Place(_npStereoRightBox, StereoBoxWidth + StereoGap, StereoBoxesTop));
            _npStereoPanel.Children.Add(Place(new Border { Height = StereoCoverSize, Child = _npStereoInfo },
                StereoRowLeft + StereoCoverSize + StereoInfoGap, 0));
            ShowAnalyzerBackground();
            Canvas.SetLeft(_npAnalyzerBox, StageCover + AnalyzerGap);
            Canvas.SetTop(_npAnalyzerBox, 0);
            _npAnalyzerTimer.Host = _npOverlay;
            _npAnalyzerTimer.Tick += (_, _) => StepAnalyzer();
            Closed += (_, _) => StopAnalyzer();
        }

        private void ApplyAnalyzerStyle(NowPlayingStyle style)
        {
            _npAnalyzerBox.IsVisible = UsesAnalyzerBox(style);
            Control visual = style switch
            {
                NowPlayingStyle.LedMatrix => _npLedMatrix,
                NowPlayingStyle.Fire => _npFire,
                _ => _npAnalyzer
            };
            if (_npAnalyzerBox.Child != visual) _npAnalyzerBox.Child = visual;
        }

        private void ApplyStereoAnalyzerStyle(bool on)
        {
            _npStereoPanel.IsVisible = on;
            UpdateNowPlayingCards();
            if (!on) return;
            _npCoverCard.Width = _npCoverCard.Height = StereoCoverSize;
            Canvas.SetLeft(_npCoverCard, StereoRowLeft);
            Canvas.SetTop(_npCoverCard, 0);
            _npCoverPlaceholder.Width = _npCoverPlaceholder.Height = 64;
        }

        private void ShowAnalyzerBackground()
        {
            bool on = _owner.AnalyzerBackground;
            _npAnalyzer.ShowBackground = _npStereoLeft.ShowBackground = _npStereoRight.ShowBackground = on;
            _npFire.ShowBackground = _npLedMatrix.ShowBackground = on;
            SetActive(_npAnalyzerBoxToggle.Icon, on);
            _npAnalyzerBox.BoxShadow = _npStereoLeftBox.BoxShadow = _npStereoRightBox.BoxShadow = on ? AnalyzerShadow : default;
        }

        private void UpdateAnalyzer()
        {
            bool run = _npOverlay.IsVisible && IsAnalyzer(NpStyle);
            string? source = run && VuInputMode() is not null ? _owner.ComputerSoundSource ?? string.Empty : null;
            if (!run || source != _npAnalyzerSourceId)
            {
                _npAnalyzerSource?.Dispose();
                _npAnalyzerSource = null;
                _npAnalyzerSourceId = null;
            }
            if (!run)
            {
                _npAnalyzerTimer.Stop();
                _npAnalyzer.Reset();
                _npStereoLeft.Reset();
                _npStereoRight.Reset();
                _npFire.Reset();
                _npLedMatrix.Reset();
                return;
            }
            if (_npAnalyzerSource == null)
            {
                _npAnalyzerSource = AudioSamples.Open(source is { Length: > 0 } ? source : null);
                _npAnalyzerSourceId = source;
            }
            if (!_npAnalyzerTimer.IsEnabled)
            {
                _npAnalyzerLast = DateTime.UtcNow;
                _npAnalyzerTimer.Start();
            }
        }

        private void StepAnalyzer()
        {
            var now = DateTime.UtcNow;
            double seconds = (now - _npAnalyzerLast).TotalSeconds;
            _npAnalyzerLast = now;
            var source = _npAnalyzerSource;
            int rate = source?.SampleRate > 0 ? source.SampleRate : 48000;
            if (NpStyle == NowPlayingStyle.StereoAnalyzer)
            {
                foreach (var (analyzer, channel) in new[] { (_npStereoLeft, AudioChannel.Left), (_npStereoRight, AudioChannel.Right) })
                {
                    if (source == null) Array.Clear(_npAnalyzerSamples);
                    else source.CopyRecent(_npAnalyzerSamples, channel);
                    analyzer.Feed(_npAnalyzerSamples, rate, seconds);
                }
                return;
            }
            if (source == null) Array.Clear(_npAnalyzerSamples);
            else source.CopyRecent(_npAnalyzerSamples);
            switch (NpStyle)
            {
                case NowPlayingStyle.LedMatrix:
                    _npLedMatrix.Feed(_npAnalyzerSamples, rate, seconds);
                    break;
                case NowPlayingStyle.Fire:
                    _npFire.Feed(_npAnalyzerSamples, rate, seconds);
                    break;
                default:
                    _npAnalyzer.Feed(_npAnalyzerSamples, rate, seconds);
                    break;
            }
        }

        private void StopAnalyzer()
        {
            _npAnalyzerTimer.Stop();
            _npAnalyzerSource?.Dispose();
            _npAnalyzerSource = null;
            _npAnalyzerSourceId = null;
        }
    }
}
