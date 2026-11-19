using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private const int SleepWarningSeconds = 60;
    private const int SleepExtraMinutes = 15;
    private const int SleepNearSeconds = 120;
    private static readonly TimeSpan SleepCheckAge = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SleepNearCheckAge = TimeSpan.FromSeconds(5);

    private readonly DispatcherTimer _sleepWatchTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, SleepWarningWindow> _sleepWarnings = new();
    private readonly Dictionary<string, DateTime> _sleepWarningHiddenUntil = new();
    private bool _sleepWatching;

    private void StartSleepWatch()
    {
        _sleepWatchTimer.Tick += async (_, _) => await CheckSleepTimersAsync();
        _sleepWatchTimer.Start();
    }

    private async Task CheckSleepTimersAsync()
    {
        if (_sleepWatching) return;
        _sleepWatching = true;
        try
        {
            var ips = _knownDevices.Select(d => d.Ip).Distinct()
                .Where(ip => !_sleepWarnings.ContainsKey(ip) && !(_sleepWarningHiddenUntil.TryGetValue(ip, out var until) && until > DateTime.UtcNow))
                .ToList();
            await Task.WhenAll(ips.Select(async ip =>
            {
                bool near = _sleepCache.TryGetValue(ip, out var cached) && cached.Seconds > 0 &&
                            cached.Seconds - (DateTime.UtcNow - cached.Fetched).TotalSeconds <= SleepNearSeconds;
                int seconds = await GetSleepSecondsAsync(ip, near ? SleepNearCheckAge : SleepCheckAge);
                if (seconds is <= 0 or > SleepWarningSeconds) return;
                _sleepCache.TryRemove(ip, out _);
                seconds = await GetSleepSecondsAsync(ip);
                if (seconds is <= 0 or > SleepWarningSeconds + 5) return;
                Dispatcher.UIThread.Post(() => ShowSleepWarning(ip, seconds));
            }));
        }
        finally
        {
            _sleepWatching = false;
        }
    }

    private void ShowSleepWarning(string ip, int seconds)
    {
        if (_sleepWarnings.ContainsKey(ip) || !_knownDevices.Any(d => d.Ip == ip)) return;
        var window = new SleepWarningWindow(DeviceNameOf(ip), seconds, !IsDeviceLocked(ip), _osdCorner, _osdScalePercent / 100.0, _sleepWarnings.Count,
            TrackToastRoom(),
            async () => await ChangeSleepTimerAsync(ip, (await GetSleepSecondsAsync(ip) + 59) / 60 + SleepExtraMinutes),
            async () => await ChangeSleepTimerAsync(ip, 0),
            () => _sleepWarningHiddenUntil[ip] = DateTime.UtcNow.AddSeconds(seconds + 10));
        window.Closed += (_, _) =>
        {
            _sleepWarnings.Remove(ip);
            var quiet = DateTime.UtcNow.AddSeconds(20);
            if (!_sleepWarningHiddenUntil.TryGetValue(ip, out var until) || until < quiet) _sleepWarningHiddenUntil[ip] = quiet;
            int slot = 0;
            foreach (var other in _sleepWarnings.Values.OrderBy(w => w.Slot)) other.Slot = slot++;
        };
        _sleepWarnings[ip] = window;
        window.Show();
    }

    private double TrackToastRoom() => _trackToast is { IsVisible: true } toast ? toast.Bounds.Height + 10 : 0;

    private void MakeRoomForTrackToast()
    {
        double room = TrackToastRoom();
        foreach (var window in _sleepWarnings.Values) window.Room = room;
    }

    private async Task ChangeSleepTimerAsync(string ip, int minutes)
    {
        if (!await SetSleepTimerAsync(ip, minutes))
            Notify($"{DeviceNameOf(ip)} didn't accept the sleep timer change. Check that it's switched on and reachable.");
    }

    private void CloseSleepWarnings()
    {
        _sleepWatchTimer.Stop();
        foreach (var window in _sleepWarnings.Values.ToList()) window.Close();
    }
}
