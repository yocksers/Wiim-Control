using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double VuPairHeight = StereoBoxWidth * VuMeter.MeterHeight / VuMeter.MeterWidth;
        private const double VuStageWidth = StereoStageWidth;
        private const double VuStageHeight = StereoBoxesTop + VuPairHeight;
        private const double VuSingleHeight = AnalyzerWidth * VuMeter.MeterHeight / VuMeter.MeterWidth;

        private readonly Canvas _npVuPanel = new() { Width = VuStageWidth, Height = VuStageHeight, IsVisible = false };
        private readonly StackPanel _npVuBottom = new() { Width = StereoInfoWidth, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        private readonly VuMeter _npVuLeft = new() { Channel = "L" };
        private readonly VuMeter _npVuRight = new() { Channel = "R" };
        private readonly VuMeter _npVuSingle = new();
        private readonly Border _npVuSingleBox = new()
        {
            Width = AnalyzerWidth, Height = VuSingleHeight, CornerRadius = new CornerRadius(16), IsVisible = false
        };
        private readonly FrameTimer _npVuTimer = new();
        private const int VuWindow = 1024;
        private IAudioSamples? _npVuLevels;
        private readonly float[] _npVuSamples = new float[VuWindow];
        private string? _npVuSource;
        private DateTime _npVuLastTick;
        private NowPlayingStyle? _npAppliedStyle;
        private readonly (Button Button, PathIcon Icon) _npVuFollowParts = IconButton(Icons.Gauge);
        private Button _npVuFollow => _npVuFollowParts.Button;
        private PathIcon _npVuFollowIcon => _npVuFollowParts.Icon;
        private const double VuButtonWidth = 38;
        private readonly Border _npVuSpacer = new() { Width = VuButtonWidth * 2, IsHitTestVisible = false };
        private readonly (Button Button, PathIcon Icon) _npVuSensitivityParts = IconButton(Icons.Tune);
        private Button _npVuSensitivity => _npVuSensitivityParts.Button;
        private readonly StackPanel _npVuButtons = new() { Orientation = Avalonia.Layout.Orientation.Horizontal };
        private readonly Flyout _npVuSensitivityFlyout = new() { Placement = PlacementMode.Top };
        private readonly Slider _npVuSensitivitySlider = new()
        {
            Orientation = Avalonia.Layout.Orientation.Vertical, Minimum = -VuSensitivityLimit, Maximum = VuSensitivityLimit, Height = 160,
            TickFrequency = 1, IsSnapToTickEnabled = true, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        private readonly TextBlock _npVuSensitivityValue = new()
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, MinWidth = 48, TextAlignment = TextAlignment.Center
        };

        private NowPlayingStyle NpStyle =>
            UsesComputerSound(_owner.NowPlayingStyle) && !VuMetersAvailable ? NowPlayingStyle.Cover : _owner.NowPlayingStyle;

        private bool VuMetersAvailable => AudioSamples.Supported && VuInputMode() is { } mode && _owner.InputFromComputer(mode);

        private bool ParecMissing => OperatingSystem.IsLinux() && !PulseMonitorCapture.Available && VuInputMode() is { } mode && _owner.InputFromComputer(mode);

        private int? VuInputMode() => _npIp is { } ip && _statuses.TryGetValue(TargetOf(ip), out var status) ? status.Mode : null;

        private void BuildVuMeters()
        {
            var shadow = BoxShadows.Parse("0 14 44 0 #C0000000");
            foreach (var (meter, left) in new[] { (_npVuLeft, 0.0), (_npVuRight, StereoBoxWidth + StereoGap) })
                _npVuPanel.Children.Add(Place(new Border
                {
                    Width = StereoBoxWidth, Height = VuPairHeight, CornerRadius = new CornerRadius(16 * StereoBoxWidth / VuMeter.MeterWidth),
                    BoxShadow = shadow, Child = meter
                }, left, StereoBoxesTop));
            _npVuPanel.Children.Add(Place(new Border { Height = StereoCoverSize, Child = _npVuBottom },
                StereoRowLeft + StereoCoverSize + StereoInfoGap, 0));
            _npVuSingleBox.CornerRadius = new CornerRadius(16 * AnalyzerWidth / VuMeter.MeterWidth);
            _npVuSingleBox.BoxShadow = shadow;
            _npVuSingleBox.Child = _npVuSingle;
            Canvas.SetLeft(_npVuSingleBox, StageCover + AnalyzerGap);
            Canvas.SetTop(_npVuSingleBox, (StageCover - VuSingleHeight) / 2);
            Grid.SetColumn(_npVuSpacer, 1);
            Grid.SetColumn(_npVuButtons, 5);
            _npVuButtons.Children.AddRange([_npVuFollow, _npVuSensitivity, _npAnalyzerBoxToggle.Button]);
            _npVuButtons.VerticalAlignment = _npVuSpacer.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            _npVuButtons.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            _npVuFollowIcon.Width = _npVuFollowIcon.Height = 20;
            _npVuSensitivityParts.Icon.Width = _npVuSensitivityParts.Icon.Height = 20;
            BuildVuSensitivityFlyout();
            _npVuFollow.Click += (_, _) =>
            {
                _owner.VuFollowVolume = !_owner.VuFollowVolume;
                ShowVuFollow();
            };
            ShowVuFollow();
            _npVuTimer.Host = this;
            _npVuTimer.Tick += (_, _) => StepVuMeters();
            Closed += (_, _) => StopVuMeters();
        }

        private static string VuSensitivityText(int value) => value == 0 ? "0 dB" : value > 0 ? $"+{value} dB" : $"−{-value} dB";

        private void BuildVuSensitivityFlyout()
        {
            var reset = new Button
            {
                Content = "Reset", FontSize = 11, Padding = new Thickness(8, 2), MinHeight = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            ToolTip.SetTip(reset, "Back to the normal sensitivity");
            reset.Click += (_, _) => _npVuSensitivitySlider.Value = 0;
            _npVuSensitivityFlyout.Content = ScaledPopup(new StackPanel
            {
                Spacing = 6, Margin = new Thickness(4, 6),
                Children =
                {
                    new TextBlock { Text = VuSensitivityText(VuSensitivityLimit), Classes = { "dim", "small" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                    _npVuSensitivitySlider,
                    new TextBlock { Text = VuSensitivityText(-VuSensitivityLimit), Classes = { "dim", "small" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                    _npVuSensitivityValue, reset
                }
            });
            _npVuSensitivitySlider.PropertyChanged += (_, e) =>
            {
                if (e.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty) return;
                int value = (int)Math.Round(_npVuSensitivitySlider.Value);
                _npVuSensitivityValue.Text = VuSensitivityText(value);
                _owner.SetVuSensitivity(value, save: false);
                _npVuLeft.Sensitivity = _npVuRight.Sensitivity = _npVuSingle.Sensitivity = value;
            };
            _npVuSensitivityFlyout.Opening += (_, _) => _npVuSensitivitySlider.Value = _owner.VuSensitivity;
            _npVuSensitivityFlyout.Closed += (_, _) => _owner.SetVuSensitivity((int)Math.Round(_npVuSensitivitySlider.Value), save: true);
            _npVuSensitivity.Flyout = _npVuSensitivityFlyout;
            ToolTip.SetTip(_npVuSensitivity, "Change how sensitive the VU meters are.");
            _npVuLeft.Sensitivity = _npVuRight.Sensitivity = _npVuSingle.Sensitivity = _owner.VuSensitivity;
        }

        private void ShowVuFollow()
        {
            bool on = _owner.VuFollowVolume;
            SetActive(_npVuFollowIcon, on);
            ToolTip.SetTip(_npVuFollow, "Switch between signal strength and volume level.");
        }

        private void SizeExtraButtonSpacer()
        {
            int shown = _npVuButtons.Children.Count(c => c.IsVisible);
            _npVuSpacer.Width = VuButtonWidth * shown + _npVuButtons.Spacing * Math.Max(0, shown - 1);
        }

        private static bool IsVuStyle(NowPlayingStyle style) => style is NowPlayingStyle.VuMeters or NowPlayingStyle.VuMeter;

        private void ApplyVuStyle(NowPlayingStyle style)
        {
            bool pair = style == NowPlayingStyle.VuMeters, any = IsVuStyle(style);
            _npVuPanel.IsVisible = pair;
            _npVuSingleBox.IsVisible = style == NowPlayingStyle.VuMeter;
            _npVuFollow.IsVisible = _npVuSensitivity.IsVisible = any;
            _npAnalyzerBoxToggle.Button.IsVisible = IsAnalyzer(style);
            _npVuButtons.IsVisible = _npVuSpacer.IsVisible = any || IsAnalyzer(style);
            SizeExtraButtonSpacer();
            if (!any && _npVuSensitivityFlyout.IsOpen) _npVuSensitivityFlyout.Hide();
            _npCoverCard.Width = _npCoverCard.Height = pair ? StereoCoverSize : StageCover;
            if (pair)
            {
                Canvas.SetLeft(_npCoverCard, StereoRowLeft);
                Canvas.SetTop(_npCoverCard, 0);
            }
            else if (NpStyle != NowPlayingStyle.Mascot)
            {
                _npCoverCard.ClearValue(Canvas.LeftProperty);
                _npCoverCard.ClearValue(Canvas.TopProperty);
            }
            _npCoverPlaceholder.Width = _npCoverPlaceholder.Height = pair ? 64 : 120;
        }

        private void CheckVuStyle()
        {
            if (!_npOverlay.IsVisible) return;
            if (NpStyle != _npAppliedStyle) ApplyNowPlayingStyle();
            else
            {
                UpdateVuMeters();
                UpdateAnalyzer();
            }
        }

        private void UpdateVuMeters()
        {
            bool run = _npOverlay.IsVisible && IsVuStyle(NpStyle);
            string? source = run && VuInputMode() is { } mode ? _owner.ComputerOutputForMode(mode) ?? string.Empty : null;
            if (!run || source != _npVuSource)
            {
                _npVuLevels?.Dispose();
                _npVuLevels = null;
                _npVuSource = null;
            }
            if (!run)
            {
                _npVuTimer.Stop();
                _npVuLeft.Reset();
                _npVuRight.Reset();
                _npVuSingle.Reset();
                return;
            }
            if (_npVuLevels == null)
            {
                _npVuLevels = AudioSamples.Open(source is { Length: > 0 } ? source : null);
                _npVuSource = source;
            }
            if (!_npVuTimer.IsEnabled)
            {
                _npVuLastTick = DateTime.UtcNow;
                _npVuTimer.Start();
            }
        }

        private void StepVuMeters()
        {
            var now = DateTime.UtcNow;
            double seconds = (now - _npVuLastTick).TotalSeconds;
            _npVuLastTick = now;
            float gain = 1;
            if (_owner.VuFollowVolume)
                gain = _npIp is { } ip && _volumes.TryGetValue(TargetOf(ip), out var volume) ? (volume.Muted ? 0 : volume.Volume / 100f) : 1;
            float left = VuLevel(AudioChannel.Left, gain);
            float right = VuLevel(AudioChannel.Right, gain);
            if (NpStyle == NowPlayingStyle.VuMeter)
            {
                _npVuSingle.Feed(Math.Max(left, right), seconds);
                return;
            }
            _npVuLeft.Feed(left, seconds);
            _npVuRight.Feed(right, seconds);
        }

        private float VuLevel(AudioChannel channel, float gain)
        {
            if (_npVuLevels == null) return 0;
            _npVuLevels.CopyRecent(_npVuSamples, channel);
            double sum = 0;
            foreach (float sample in _npVuSamples) sum += sample * sample;
            return (float)Math.Sqrt(sum / _npVuSamples.Length) * gain;
        }

        private void StopVuMeters()
        {
            _npVuTimer.Stop();
            _npVuLevels?.Dispose();
            _npVuLevels = null;
            _npVuSource = null;
        }
    }
}
