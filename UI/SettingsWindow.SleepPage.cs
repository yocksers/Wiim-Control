using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly StackPanel _sleepRows = new();
        private readonly TextBlock _lblSleepStatus = Subtitle();
        private readonly DispatcherTimer _sleepClock = new() { Interval = TimeSpan.FromSeconds(30) };
        private int _sleepVersion;
        private const int EndOfTrack = -1;
        private bool _sleepFilling;

        private Control BuildSleepPage()
        {
            var refresh = Pill("Refresh");
            refresh.Click += async (_, _) => await RefreshSleepAsync();
            var page = Page("Sleep timer", _lblSleepStatus, [refresh], _sleepRows);
            page.PropertyChanged += async (_, e) =>
            {
                if (e.Property != IsVisibleProperty) return;
                if (!page.IsVisible)
                {
                    _sleepClock.Stop();
                    return;
                }
                _sleepClock.Start();
                await RefreshSleepAsync();
            };
            _sleepClock.Tick += async (_, _) => await RefreshSleepAsync(quiet: true);
            return page;
        }

        private static string SleepLeft(int seconds)
        {
            int minutes = (seconds + 59) / 60;
            return minutes >= 60 ? $"Stops in {minutes / 60} h {minutes % 60} min" : $"Stops in {minutes} min";
        }

        private async Task RefreshSleepAsync(bool quiet = false)
        {
            int version = ++_sleepVersion;
            var devices = _owner._knownDevices.ToList();
            if (devices.Count == 0)
            {
                _lblSleepStatus.Text = string.Empty;
                SetRows(_sleepRows, [Muted("Add a WiiM device on the Devices page first.")]);
                return;
            }
            if (!quiet) _lblSleepStatus.Text = "Loading…";
            var secondsTask = Task.WhenAll(devices.Select(d => _owner.GetSleepSecondsAsync(d.Ip)));
            var endOfTrackTask = Task.WhenAll(devices.Select(d => _owner.GetEndOfTrackAsync(d.Ip)));
            var seconds = await secondsTask;
            var endOfTrack = await endOfTrackTask;
            if (version != _sleepVersion) return;
            _lblSleepStatus.Text = string.Empty;

            var rows = new List<Control>();
            _sleepFilling = true;
            for (int i = 0; i < devices.Count; i++)
            {
                string ip = devices[i].Ip;
                int left = seconds[i];
                bool? trackEnd = endOfTrack[i];
                var choice = new ComboBox { Width = FieldWidth, PlaceholderText = "Change…" };
                choice.Items.Add(ComboItem(0, "Off"));
                foreach (int minutes in SleepChoices) choice.Items.Add(ComboItem(minutes, $"{minutes} minutes"));
                if (trackEnd != null) choice.Items.Add(ComboItem(EndOfTrack, "End of current track"));
                choice.SelectedIndex = trackEnd == true ? choice.Items.Count - 1 : left > 0 ? -1 : 0;
                choice.SelectionChanged += async (_, _) =>
                {
                    if (_sleepFilling || (choice.SelectedItem as ComboBoxItem)?.Tag is not int minutes) return;
                    bool ok = minutes == EndOfTrack
                        ? (left <= 0 || await _owner.SetSleepTimerAsync(ip, 0)) && await _owner.SetEndOfTrackAsync(ip, true)
                        : (trackEnd != true || await _owner.SetEndOfTrackAsync(ip, false)) && await _owner.SetSleepTimerAsync(ip, minutes);
                    if (!ok)
                        await Dialogs.MessageAsync(this, "The device didn't accept the sleep timer. Check that it's switched on and reachable.", _scale);
                    await RefreshSleepAsync(quiet: true);
                };
                string stateText = trackEnd == true ? "Stops after this track" : left > 0 ? SleepLeft(left) : string.Empty;
                var state = new TextBlock
                {
                    Text = stateText, Classes = { "accent" }, IsVisible = stateText.Length > 0,
                    VerticalAlignment = VerticalAlignment.Center
                };
                rows.Add(LabeledRow(devices[i].Name, new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Right, Children = { state, choice }
                }));
            }
            _sleepFilling = false;
            SetRows(_sleepRows, [Card("Sleep timer", rows.ToArray())]);
        }
    }
}
