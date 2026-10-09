using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace WiimControl;

sealed class PlayingIndicator : Control
{
    private const int BarCount = 4;
    private const double Gap = 2;
    private static readonly double[] Speeds = [5.3, 7.9, 4.4, 6.7];
    private static readonly double[] Phases = [0, 1.9, 3.3, 4.8];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _attached;

    public IBrush Foreground { get; set; } = Palette.Text;

    public PlayingIndicator()
    {
        Width = 14;
        Height = 12;
        IsVisible = false;
        _timer.Tick += (_, _) => { if (IsEffectivelyVisible) InvalidateVisual(); };
    }

    public override void Render(DrawingContext context)
    {
        double barWidth = (Bounds.Width - Gap * (BarCount - 1)) / BarCount;
        double t = _clock.Elapsed.TotalSeconds;
        for (int i = 0; i < BarCount; i++)
        {
            double wave = (Math.Sin(t * Speeds[i] + Phases[i]) + Math.Sin(t * Speeds[i] * 0.61 + Phases[i] * 1.7)) / 2;
            double height = Bounds.Height * (0.2 + 0.8 * Math.Abs(wave));
            context.DrawRectangle(Foreground, null,
                new Rect(i * (barWidth + Gap), Bounds.Height - height, barWidth, height), 1, 1);
        }
    }

    private void UpdateTimer()
    {
        bool run = _attached && IsVisible;
        if (run && !_timer.IsEnabled) _timer.Start();
        else if (!run && _timer.IsEnabled) _timer.Stop();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        UpdateTimer();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) UpdateTimer();
    }
}
