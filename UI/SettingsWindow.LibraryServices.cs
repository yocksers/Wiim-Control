using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private static readonly TimeSpan ServiceSettleTime = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ServiceCheckInterval = TimeSpan.FromMilliseconds(500);
        private const int ServiceChecks = 12;
        private const string AutoConnectSetting = "AutoConnect";

        private sealed record ServiceChange(bool Value, Func<bool, Task<bool>> Apply, Func<Task<bool?>> Read);

        private readonly Panel _libraryServiceHost = new();
        private readonly Dictionary<(string Ip, string Name), ServiceChange> _serviceChanges = new();
        private int _libraryServiceVersion;
        private bool _libraryServiceBusy;
        private bool _serviceChangesRunning;

        private async Task RefreshLibraryServiceAsync()
        {
            int version = ++_libraryServiceVersion;
            var server = SelectedLibraryServer;
            if (AmpOf(_cboLibraryAmp) is not { } ip)
            {
                _libraryServiceHost.Children.Clear();
                return;
            }
            string device = DeviceName(ip);
            var lyrionServer = server is { IsLyrion: true } ? server : null;
            bool lyrion = lyrionServer != null;
            var statesTask = _owner.GetStreamServiceStatesAsync(ip);
            var squeezeTask = lyrion ? _owner.GetSqueezeliteStateAsync(ip) : Task.FromResult<SqueezeliteState?>(null);
            await Task.WhenAll(statesTask, squeezeTask);
            if (version != _libraryServiceVersion) return;
            var (squeezelite, roon, answered) = statesTask.Result;
            if (answered && squeezelite == null && roon == null)
            {
                _libraryServiceHost.Children.Clear();
                return;
            }
            squeezelite = WantedSetting(ip, SqueezeliteService, squeezelite);
            roon = WantedSetting(ip, RoonReadyService, roon);

            var switches = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 28 };
            if (squeezelite is bool squeezeOn)
                switches.Children.Add(ServiceToggle(ip, SqueezeliteService, "Squeezelite", squeezeOn,
                    on => _owner.SetStreamServiceActiveAsync(ip, SqueezeliteService, on), () => _owner.GetStreamServiceActiveAsync(ip, SqueezeliteService),
                    $"Lets {device} play from a Lyrion Music Server"));
            if (roon is bool roonOn)
                switches.Children.Add(ServiceToggle(ip, RoonReadyService, "Roon Ready", roonOn,
                    on => _owner.SetStreamServiceActiveAsync(ip, RoonReadyService, on), () => _owner.GetStreamServiceActiveAsync(ip, RoonReadyService),
                    $"Lets Roon play to {device}. When it's off, the device doesn't show up in Roon."));
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            header.Children.Add(new TextBlock
            {
                Text = $"Streaming services on {device}", Classes = { "heading" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
            });
            Control right = switches.Children.Count > 0 ? switches : Muted("Not reported. Check that the device is switched on and reachable.");
            Grid.SetColumn(right, 1);
            header.Children.Add(right);

            var rows = new List<Control> { header };
            if (server is { IsRoon: true } && roon == false)
                rows.Add(Muted($"Switch Roon Ready on so {device} shows up in Roon."));
            if (lyrion && squeezelite == false)
                rows.Add(Muted($"Switch Squeezelite on so {device} can play from {lyrionServer!.Name}."));
            else if (lyrionServer != null && squeezelite == true && squeezeTask.Result is { } state)
            {
                string host = new Uri(lyrionServer.ContentDirectoryUrl).Host;
                bool connectedHere = state.State == "connected" && ServerHost(state.ConnectedServer) == host;
                string status = connectedHere ? $"Connected to {lyrionServer.Name}"
                    : state.State == "connected" && state.ConnectedServer.Length > 0 ? $"Connected to {ServerHost(state.ConnectedServer)}"
                    : state.State == "discovering" ? "Looking for servers"
                    : "Not connected";
                Control statusValue = Info(status);
                if (!connectedHere)
                {
                    var connect = Pill($"Connect to {lyrionServer.Name}", accent: true);
                    connect.Click += async (_, _) => await RunLibraryServiceAsync(async () =>
                        await _owner.ConnectSqueezeliteAsync(ip, host) && await _owner.WaitForLyrionPlayerAsync(ip, lyrionServer));
                    statusValue = new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { Info(status), connect }
                    };
                }
                rows.Add(LabeledRow("Status", statusValue));
                rows.Add(ServiceToggle(ip, AutoConnectSetting, "Reconnect automatically", state.AutoConnect,
                    on => _owner.SetSqueezeliteAutoConnectAsync(ip, on), async () => (await _owner.GetSqueezeliteStateAsync(ip))?.AutoConnect,
                    "Connects again to the last server when Squeezelite starts"));
            }
            var list = new StackPanel { Spacing = 4 };
            foreach (var row in rows) list.Children.Add(row);
            ShowLibraryService(new Border { Classes = { "card" }, Child = list });
        }

        private void ShowLibraryService(Control card)
        {
            _libraryServiceHost.Children.Clear();
            _libraryServiceHost.Children.Add(card);
        }

        private bool? WantedSetting(string ip, string name, bool? reported) =>
            _serviceChanges.TryGetValue((ip, name), out var change) ? change.Value : reported;

        private Grid ServiceToggle(string ip, string name, string text, bool value, Func<bool, Task<bool>> apply, Func<Task<bool?>> read,
            string? tip = null)
        {
            var toggle = new ToggleSwitch { IsChecked = WantedSetting(ip, name, value) };
            toggle.IsCheckedChanged += async (_, _) =>
            {
                _serviceChanges[(ip, name)] = new ServiceChange(toggle.IsChecked == true, apply, read);
                await ApplyServiceChangesAsync();
            };
            var row = ToggleRow(text, toggle);
            if (tip != null) ToolTip.SetTip(row, tip);
            return row;
        }

        private async Task ApplyServiceChangesAsync()
        {
            if (_serviceChangesRunning) return;
            _serviceChangesRunning = true;
            bool failed = false;
            try
            {
                while (_serviceChanges.Count > 0)
                {
                    var (key, change) = _serviceChanges.First();
                    bool ok = await change.Apply(change.Value) && await WaitForSettingAsync(change);
                    if (!_serviceChanges.TryGetValue(key, out var latest) || !ReferenceEquals(latest, change)) continue;
                    _serviceChanges.Remove(key);
                    failed |= !ok;
                }
            }
            finally { _serviceChangesRunning = false; }
            if (failed) await Dialogs.MessageAsync(this, "The device didn't accept the change.", _scale);
            await RefreshLibraryServiceAsync();
        }

        private static async Task<bool> WaitForSettingAsync(ServiceChange change)
        {
            for (int i = 0; i < ServiceChecks; i++)
            {
                if (await change.Read() == change.Value) return true;
                await Task.Delay(ServiceCheckInterval);
            }
            return false;
        }

        private async Task RunLibraryServiceAsync(Func<Task<bool>> action)
        {
            if (_libraryServiceBusy) return;
            _libraryServiceBusy = true;
            bool ok;
            try
            {
                ok = await action();
                if (ok) await Task.Delay(ServiceSettleTime);
            }
            finally { _libraryServiceBusy = false; }
            if (!ok) await Dialogs.MessageAsync(this, "The device didn't accept the request.", _scale);
            await RefreshLibraryServiceAsync();
        }
    }
}
