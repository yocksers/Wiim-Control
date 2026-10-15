using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

        private static string FormatClock(TimeSpan time) => Invariant($"{time.Hours:00}:{time.Minutes:00}");

        private static Border BuildDeviceCard(DeviceInfo info)
        {
            var rows = new List<Control> { LabeledRow("Model", Info(info.Model)), LabeledRow("Firmware", Info(info.Firmware)) };
            if (info.CpuTemperature is int cpu) rows.Add(LabeledRow("Processor temperature", Info($"{cpu} °C")));
            if (info.BoardTemperature is int board) rows.Add(LabeledRow("Board temperature", Info($"{board} °C")));
            if (info.Update is { } update)
            {
                if (update.HasAmpUpdate)
                    rows.Add(LabeledRow("Firmware update", new TextBlock
                    {
                        Text = $"{FirmwareUpdate.ShortVersion(update.Available)} is available", Classes = { "accent" },
                        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
                    }));
                if (update.HasRemoteUpdate)
                    rows.Add(LabeledRow("Remote control update", new TextBlock
                    {
                        Text = update.RemoteAvailable == "new" ? "Available" : $"{update.RemoteAvailable} is available", Classes = { "accent" },
                        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
                    }));
                rows.Add(Muted("Install updates from the WiiM Home app. Don't switch the amp off while it updates."));
            }
            return Card("Device", rows.ToArray());
        }

        private static string ServerHost(string address) => address.EndsWith(":3483", StringComparison.Ordinal) ? address[..^5] : address;

        private Border BuildSqueezeliteCard(string ip, SqueezeliteState state)
        {
            string status = state.State switch
            {
                "connected" when state.ConnectedServer.Length > 0 => $"Connected to {ServerHost(state.ConnectedServer)}",
                "discovering" => "Looking for servers",
                _ => "Not connected"
            };
            var servers = new[] { state.ConnectedServer, state.DefaultServer }.Concat(state.Found)
                .Where(s => s.Length > 0).Distinct().ToList();
            var server = new ComboBox { Width = 200, PlaceholderText = "No server found" };
            foreach (var s in servers) server.Items.Add(ComboItem(s, ServerHost(s)));
            server.SelectedIndex = servers.Count > 0 ? 0 : -1;

            var search = Pill("Search");
            ToolTip.SetTip(search, "Look for Lyrion Music Servers on the network");
            search.Click += async (_, _) => await ChangeAmpAsync(async () =>
            {
                bool ok = await _owner.DiscoverSqueezeliteAsync(ip);
                await Task.Delay(3000);
                return ok;
            });
            var connect = Pill("Connect");
            connect.IsEnabled = servers.Count > 0;
            connect.Click += async (_, _) =>
            {
                if ((server.SelectedItem as ComboBoxItem)?.Tag is not string address) return;
                await ChangeAmpAsync(async () =>
                {
                    bool ok = await _owner.ConnectSqueezeliteAsync(ip, address);
                    await Task.Delay(2500);
                    return ok;
                });
            };
            var serverRow = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                Children = { search, connect, server }
            };

            return Card("Squeezelite (Lyrion Music Server)",
                Muted("Squeezelite lets the amp play from a Lyrion Music Server. It's switched on or off in the WiiM Home app."),
                LabeledRow("Status", Info(status)),
                LabeledRow("Server", serverRow),
                AmpToggle("Connect automatically", state.AutoConnect, on => _owner.SetSqueezeliteAutoConnectAsync(ip, on)));
        }

        private Border BuildSubwooferCard(string ip, SubwooferInfo sub)
        {
            var rows = new List<Control>
            {
                AmpToggle("Subwoofer output", sub.Enabled, on => _owner.SetSubwooferAsync(ip, "status", on ? 1 : 0)),
                LabeledRow("Subwoofer connected", Info(sub.Plugged ? "Yes" : "No"))
            };

            var level = AmpSlider(-15, 15, (int)Math.Round(sub.Level), v => Invariant($"{v:+0;-0;0} dB"), true, out var levelRow);
            level.ValueCommitted += async (_, _) => await ChangeAmpAsync(() => _owner.SetSubwooferAsync(ip, "level", level.Value));
            rows.Add(LabeledRow("Level", levelRow));

            var cross = AmpSlider(30, 250, (int)Math.Round(sub.Crossover), v => $"{v} Hz", false, out var crossRow);
            cross.ValueCommitted += async (_, _) => await ChangeAmpAsync(() => _owner.SetSubwooferAsync(ip, "cross", cross.Value));
            rows.Add(LabeledRow("Crossover", crossRow));

            rows.Add(AmpChoice("Phase", [(0, "0°"), (180, "180°")], sub.Phase >= 90 ? 180 : 0,
                phase => _owner.SetSubwooferAsync(ip, "phase", phase)));

            if (sub.Delay is double delay)
            {
                var delaySlider = AmpSlider(-2000, 2000, (int)Math.Round(delay * 10), v => Invariant($"{v / 10.0:0.0} ms"), true, out var delayRow);
                delaySlider.ValueCommitted += async (_, _) =>
                    await ChangeAmpAsync(() => _owner.SetSubwooferAsync(ip, "sub_delay", delaySlider.Value / 10.0));
                rows.Add(LabeledRow("Delay", delayRow));
            }

            rows.Add(LabeledRow("Main speakers high-pass", Info(sub.MainHighPass ? "On" : "Off")));
            rows.Add(Muted("The main speakers' high-pass filter can only be changed in the WiiM Home app."));
            return Card("Subwoofer", rows.ToArray());
        }

        private static string TriggerName(string trigger) => trigger switch
        {
            "EveryDay" => "every day",
            "Once" => "once",
            "EveryWeek" or "Week" => "every week",
            "EveryMonth" or "Month" => "every month",
            _ => trigger
        };

        private Border BuildAlarmsCard(string ip, List<AlarmInfo> alarms)
        {
            var rows = new List<Control>
            {
                Muted("An alarm starts playing a web address, such as an internet radio stream, every day at the chosen time. " +
                      "Alarms made in the WiiM Home app are shown here too.")
            };
            foreach (var alarm in alarms) rows.Add(BuildAlarmRow(ip, alarm));
            return Card("Alarms", rows.ToArray());
        }

        private MenuFlyout StationMenu(Action<RadioStation> chosen)
        {
            var menu = new MenuFlyout();
            menu.Opening += (_, _) =>
            {
                menu.Items.Clear();
                void Add(string header, IEnumerable<RadioStation> stations)
                {
                    var list = stations.ToList();
                    if (list.Count == 0) return;
                    if (menu.Items.Count > 0) menu.Items.Add(new Separator());
                    menu.Items.Add(new MenuItem { Header = header, IsEnabled = false });
                    foreach (var s in list)
                    {
                        var item = new MenuItem { Header = s.Name };
                        item.Click += (_, _) => chosen(s);
                        menu.Items.Add(item);
                    }
                }
                Add("Favourites", _owner.RadioFavorites.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase));
                Add("Recently played", _owner.Recent.Where(r => r.Station != null).Select(r => r.Station!)
                    .Where(s => !_owner.IsRadioFavorite(s)).Take(8));
                Add("Radio Paradise", RadioDirectory.RadioParadise);
            };
            return menu;
        }

        private Control BuildAlarmRow(string ip, AlarmInfo alarm)
        {
            bool daily = alarm.Trigger.Equals("EveryDay", StringComparison.OrdinalIgnoreCase);
            TimeSpan? local = alarm.TimeUtc is { } utc ? UtcToLocalTime(utc) : null;
            string clock = local is { } t ? FormatClock(t) : string.Empty;
            string status = !alarm.Enabled ? "Off"
                : daily ? $"On, every day at {clock}"
                : $"On, {TriggerName(alarm.Trigger)} at {clock} (made in the WiiM Home app)";

            var heading = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children = { new TextBlock { Text = $"Alarm {alarm.Slot + 1}" }, new TextBlock { Text = status, Classes = { "muted" } } }
            };
            var time = new TextBox { Width = 80, Text = alarm.Enabled && daily && clock.Length > 0 ? clock : "07:00", Watermark = "07:00" };
            var url = new TextBox
            {
                Text = alarm.Enabled && daily ? alarm.Path : string.Empty, Watermark = "Stream address, for example https://…",
                Margin = new Thickness(8, 0)
            };
            var station = Pill("Station ▾");
            station.Margin = new Thickness(0, 0, 8, 0);
            ToolTip.SetTip(station, "Pick one of your radio favourites or a Radio Paradise channel");
            station.Flyout = StationMenu(chosen => url.Text = chosen.Url);
            var save = Pill(alarm.Enabled ? "Save" : "Turn on");
            var remove = Pill("Turn off");
            remove.IsEnabled = alarm.Enabled;
            remove.Margin = new Thickness(8, 0, 0, 0);

            save.Click += async (_, _) =>
            {
                string text = time.Text?.Trim().Replace('.', ':') ?? string.Empty;
                if (!TimeSpan.TryParseExact(text, [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out var alarmTime) || alarmTime.TotalHours >= 24)
                {
                    await Dialogs.MessageAsync(this, "Please enter the time as hours and minutes, for example 07:30.", _scale);
                    return;
                }
                string address = url.Text?.Trim() ?? string.Empty;
                if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https") ||
                    address.Contains(' ') || address.Length > 250)
                {
                    await Dialogs.MessageAsync(this, "Please enter a web address that starts with http:// or https://, for example an internet radio stream.", _scale);
                    return;
                }
                if (alarm.Enabled && !daily &&
                    !await Dialogs.ConfirmAsync(this, $"Alarm {alarm.Slot + 1} was made in the WiiM Home app. Replace it with this daily alarm?", _scale)) return;
                await ChangeAmpAsync(() => _owner.SetDailyAlarmAsync(ip, alarm.Slot, alarmTime, address));
            };
            remove.Click += async (_, _) =>
            {
                if (!await Dialogs.ConfirmAsync(this, $"Turn off alarm {alarm.Slot + 1}? This removes it from the amp.", _scale)) return;
                await ChangeAmpAsync(() => _owner.ClearAlarmAsync(ip, alarm.Slot));
            };

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(time);
            Grid.SetColumn(url, 1);
            row.Children.Add(url);
            Grid.SetColumn(station, 2);
            row.Children.Add(station);
            Grid.SetColumn(save, 3);
            row.Children.Add(save);
            Grid.SetColumn(remove, 4);
            row.Children.Add(remove);
            return new StackPanel { Margin = new Thickness(0, 8, 0, 4), Children = { heading, row } };
        }
    }
}
