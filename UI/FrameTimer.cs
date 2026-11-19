using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace WiimControl;

sealed class FrameTimer
{
    private static readonly TimeSpan Fallback = TimeSpan.FromMilliseconds(16);
    private int _generation;

    public Visual? Host { get; set; }
    public bool IsEnabled { get; private set; }
    public event EventHandler? Tick;

    public void Start()
    {
        if (IsEnabled) return;
        IsEnabled = true;
        Request(++_generation);
    }

    public void Stop() => IsEnabled = false;

    private void Request(int generation)
    {
        if (Host != null && TopLevel.GetTopLevel(Host) is { } top) top.RequestAnimationFrame(_ => Next(generation));
        else DispatcherTimer.RunOnce(() => Next(generation), Fallback);
    }

    private void Next(int generation)
    {
        if (!IsEnabled || generation != _generation) return;
        Tick?.Invoke(this, EventArgs.Empty);
        if (IsEnabled && generation == _generation) Request(generation);
    }
}
