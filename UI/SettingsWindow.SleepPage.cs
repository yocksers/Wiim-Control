using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed class SleepPage(SettingsWindow window)
        {
            private const int EndOfTrack = -1;
            private readonly StackPanel _rows = new();
            private readonly TextBlock _status = Subtitle();
            private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(30) };
            private int _version;
            private bool _filling;

            public Control Build()
            {
                var refresh = Pill("Refresh");
                refresh.Click += async (_, _) => await RefreshAsync();
                var page = window.Page("Sleep timer", _status, [refresh], _rows);
                page.PropertyChanged += async (_, e) =>
                {
                    if (e.Property != IsVisibleProperty) return;
                    if (!page.IsVisible)
                    {
                        _clock.Stop();
                        return;
                    }
                    _clock.Start();
                    await RefreshAsync();
                };
                _clock.Tick += async (_, _) => await RefreshAsync(quiet: true);
                window.Closed += (_, _) => _clock.Stop();
                return page;
            }

            private static string TimeLeft(int seconds)
            {
                int minutes = (seconds + 59) / 60;
                return minutes >= 60 ? $"Stops in {minutes / 60} h {minutes % 60} min" : $"Stops in {minutes} min";
            }

            private async Task RefreshAsync(bool quiet = false)
            {
                var owner = window._owner;
                int version = ++_version;
                var devices = owner._knownDevices.ToList();
                if (devices.Count == 0)
                {
                    _status.Text = string.Empty;
                    SetRows(_rows, [Muted("Add a WiiM device on the Devices page first.")]);
                    return;
                }
                if (!quiet) _status.Text = "Loading…";
                var secondsTask = Task.WhenAll(devices.Select(d => owner.GetSleepSecondsAsync(d.Ip)));
                var endOfTrackTask = Task.WhenAll(devices.Select(d => owner.GetEndOfTrackAsync(d.Ip)));
                var seconds = await secondsTask;
                var endOfTrack = await endOfTrackTask;
                if (version != _version) return;
                _status.Text = string.Empty;

                var rows = new List<Control>();
                _filling = true;
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
                        if (_filling || (choice.SelectedItem as ComboBoxItem)?.Tag is not int minutes) return;
                        bool ok = minutes == EndOfTrack
                            ? (left <= 0 || await owner.SetSleepTimerAsync(ip, 0)) && await owner.SetEndOfTrackAsync(ip, true)
                            : (trackEnd != true || await owner.SetEndOfTrackAsync(ip, false)) && await owner.SetSleepTimerAsync(ip, minutes);
                        if (!ok)
                            await Dialogs.MessageAsync(window, "The device didn't accept the sleep timer. Check that it's switched on and reachable.", window._scale);
                        await RefreshAsync(quiet: true);
                    };
                    string stateText = trackEnd == true ? "Stops after this track" : left > 0 ? TimeLeft(left) : string.Empty;
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
                _filling = false;
                SetRows(_rows, [Card("Sleep timer", rows.ToArray())]);
            }
        }
    }
}
