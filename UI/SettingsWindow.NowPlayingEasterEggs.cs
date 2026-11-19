using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private const double ScratchLimit = 150;
        private const int TapeClicks = 4;
        private static readonly TimeSpan TapeClickWindow = TimeSpan.FromSeconds(1.5);

        private bool _npTapeSpilling;

        private void BuildEasterEggs()
        {
            HookScratch(_npDisc, _npLabelSpin.Target);
            HookScratch(_npBigDisc, _npBigLabelSpin.Target);
            HookTapeSpill();
        }

        private static void Animate(Visual host, TimeSpan duration, Action<double> step, Action? done = null)
        {
            var start = DateTime.UtcNow;
            var timer = new FrameTimer { Host = host };
            timer.Tick += (_, _) =>
            {
                double progress = Math.Min(1, (DateTime.UtcNow - start).TotalSeconds / duration.TotalSeconds);
                step(progress);
                if (progress < 1) return;
                timer.Stop();
                done?.Invoke();
            };
            step(0);
            timer.Start();
        }

        private static void HookScratch(Control record, Panel label)
        {
            var scratch = new RotateTransform();
            label.RenderTransform = scratch;
            double? grip = null;
            double Angle(PointerEventArgs e)
            {
                var p = e.GetPosition(record);
                return Math.Atan2(p.Y - record.Bounds.Height / 2, p.X - record.Bounds.Width / 2) * 180 / Math.PI;
            }
            record.PointerPressed += (_, e) =>
            {
                grip = Angle(e);
                e.Pointer.Capture(record);
            };
            record.PointerMoved += (_, e) =>
            {
                if (grip is not { } from) return;
                double angle = Angle(e), turn = angle - from;
                if (turn > 180) turn -= 360;
                if (turn < -180) turn += 360;
                grip = angle;
                scratch.Angle = Math.Clamp(scratch.Angle + turn, -ScratchLimit, ScratchLimit);
            };
            void Release()
            {
                if (grip == null) return;
                grip = null;
                double from = scratch.Angle;
                Animate(record, TimeSpan.FromMilliseconds(350), p => { if (grip == null) scratch.Angle = from * (1 - Ease(p)); });
            }
            record.PointerReleased += (_, e) =>
            {
                e.Pointer.Capture(null);
                Release();
            };
            record.PointerCaptureLost += (_, _) => Release();
        }

        private void HookTapeSpill()
        {
            var strands = new[]
            {
                "M 282,398 C 262,450 222,500 258,522 C 296,546 332,486 318,456 C 304,428 356,424 372,476 C 386,522 344,540 336,512 C 330,488 352,444 360,398",
                "M 300,398 C 296,430 268,470 290,488 C 312,504 330,470 322,446 C 316,430 334,420 342,398"
            };
            var spill = new Canvas { IsHitTestVisible = false, Opacity = 0 };
            foreach (string strand in strands)
                spill.Children.Add(new Path { Data = Geometry.Parse(strand), Stroke = TapeBrush, StrokeThickness = 3, StrokeJoin = PenLineJoin.Round });
            var stretch = new ScaleTransform(1, 0);
            var sway = new RotateTransform();
            spill.RenderTransform = new TransformGroup { Children = { stretch, sway } };
            spill.RenderTransformOrigin = new RelativePoint(320, 398, RelativeUnit.Absolute);
            _npCassette.Children.Add(spill);

            var clicks = new List<DateTime>();
            _npCassette.PointerPressed += (_, _) =>
            {
                var now = DateTime.UtcNow;
                clicks.RemoveAll(t => now - t > TapeClickWindow);
                clicks.Add(now);
                if (clicks.Count < TapeClicks || _npTapeSpilling) return;
                clicks.Clear();
                _npTapeSpilling = true;
                spill.Opacity = 1;
                Animate(spill, TimeSpan.FromMilliseconds(600), p => stretch.ScaleY = Ease(p), () =>
                    Animate(spill, TimeSpan.FromMilliseconds(1400), p => sway.Angle = Math.Sin(p * Math.PI * 3) * 6 * (1 - p), () =>
                        Animate(spill, TimeSpan.FromMilliseconds(900), p => stretch.ScaleY = 1 - Ease(p), () =>
                        {
                            spill.Opacity = 0;
                            _npTapeSpilling = false;
                        })));
            };
        }
    }
}
