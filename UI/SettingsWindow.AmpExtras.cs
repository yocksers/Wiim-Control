using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

        private static string FormatClock(TimeSpan time) => Invariant($"{time.Hours:00}:{time.Minutes:00}");

        private static TextBlock Selectable(string text) => new SelectableTextBlock
        {
            Text = text, Foreground = Palette.TextMuted, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };

        private Border BuildDeviceCard(string ip, DeviceInfo info, AutoUpdate? autoUpdate)
        {
            var rows = new List<Control> { LabeledRow("Model", Info(info.Model)), LabeledRow("Firmware", Info(info.Firmware)) };
            if (info.Ssid.Length > 0) rows.Add(LabeledRow("Device's own Wi-Fi name (SSID)", Selectable(info.Ssid)));
            if (info.Mac.Length > 0) rows.Add(LabeledRow("MAC address", Selectable(info.Mac)));
            if (info.Uuid.Length > 0) rows.Add(LabeledRow("UUID", Selectable(info.Uuid)));
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
                rows.Add(Muted("Install updates from the WiiM Home app. Don't switch the device off while it updates."));
            }
            if (autoUpdate is { } auto)
            {
                var choices = new List<(int Value, string Label)> { (0, "On") };
                choices.AddRange(AutoUpdatePauseMonths.Select(m => (m, $"Paused for {m} months")));
                if (!choices.Any(c => c.Value == auto.Months)) choices.Add((auto.Months, auto.Months < 0 ? "Off" : $"Paused for {auto.Months} months"));
                var autoRow = AmpChoice("Automatic updates", choices, auto.Months, months => _owner.SetAutoUpdateAsync(ip, months));
                ToolTip.SetTip(autoRow, "Whether the device installs firmware updates by itself, or pauses them for a while");
                rows.Add(autoRow);
                if (auto.Months > 0 && auto.PausedUntil is { } until)
                    rows.Add(Muted($"Automatic updates are paused until {until:d}. No periodic updates are installed before then."));
            }
            var restart = Pill("Restart");
            ToolTip.SetTip(restart, "Restarts the device. It stops playing and is back after about a minute.");
            restart.Click += async (_, _) => await RestartDeviceAsync(ip);
            rows.Add(LabeledRow("Restart device", restart));
            var restore = Pill("Restore…");
            restore.Click += async (_, _) => await RestoreFactoryAsync(ip);
            rows.Add(LabeledRow("Restore factory settings", restore));
            return Card("Device", rows.ToArray());
        }

        private async Task RestartDeviceAsync(string ip)
        {
            string name = _owner._knownDevices.FirstOrDefault(d => d.Ip == ip).Name ?? ip;
            if (!await Dialogs.ConfirmAsync(this, $"Restart {name}? It stops playing and is back after about a minute.", _scale)) return;
            string message = await _owner.RestartDeviceAsync(ip) switch
            {
                true => $"{name} is restarting. It's back after about a minute.",
                false => "The device didn't accept the request.",
                null => $"Couldn't reach {name}. Check that it's switched on and connected to the network."
            };
            await Dialogs.MessageAsync(this, message, _scale);
        }

        private async Task RestoreFactoryAsync(string ip)
        {
            string name = _owner._knownDevices.FirstOrDefault(d => d.Ip == ip).Name ?? ip;
            if (!await Dialogs.ConfirmAsync(this,
                    $"Restore {name} to its factory settings?\n\nThis erases all its settings, including its Wi-Fi connection, presets, EQ presets, " +
                    "RoomFit profiles and name. Afterwards you have to set it up again in the WiiM Home app.", _scale)) return;
            int choice = await Dialogs.ButtonsAsync(this,
                $"Are you sure? {name} disconnects from your network and has to be set up again in the WiiM Home app. " +
                "Tip: back up its presets on the General page first.", _scale, "Cancel", "Restore factory settings");
            if (choice != 1) return;
            if (await _owner.RestoreFactorySettingsAsync(ip))
                await Dialogs.MessageAsync(this, $"{name} is restoring its factory settings and restarts. Set it up again in the WiiM Home app.", _scale);
            else
                await Dialogs.MessageAsync(this, "The device didn't accept the request.", _scale);
        }

        private static readonly int[] StandbyMinutes = [5, 10, 15, 20, 30, 45, 60, 90, 120];

        private Border BuildStandbyCard(string ip, int seconds)
        {
            var combo = new ComboBox { Width = 200 };
            var choices = StandbyMinutes.Select(m => m * 60).ToList();
            if (!choices.Contains(seconds)) choices.Add(seconds);
            choices.Sort();
            foreach (int s in choices)
                combo.Items.Add(ComboItem(s, s % 3600 == 0 && s >= 3600 ? (s == 3600 ? "1 hour" : $"{s / 3600} hours")
                    : s % 60 == 0 ? $"{s / 60} minutes" : $"{s} seconds"));
            combo.SelectedIndex = choices.IndexOf(seconds);
            combo.SelectionChanged += async (_, _) =>
            {
                if ((combo.SelectedItem as ComboBoxItem)?.Tag is int value && value != seconds)
                    await ChangeAmpAsync(() => _owner.SetStandbyAsync(ip, value));
            };
            return Card("Standby", LabeledRow("Go to standby after nothing has played for", combo));
        }

        private Border BuildNetworkCard(string ip, NetworkInfo net)
        {
            var rows = new List<Control>();
            if (net.Wired)
            {
                rows.Add(LabeledRow("Connection", Info("Ethernet")));
                rows.Add(LabeledRow("IP address", Selectable(net.Ip)));
            }
            else
            {
                rows.Add(LabeledRow("Wi-Fi network", Info(net.WifiName)));
                rows.Add(LabeledRow("IP address", Selectable(net.Ip)));
                if (net.Rssi is int rssi)
                {
                    var strength = new TextBlock
                    {
                        Text = $"{SignalName(rssi)} ({rssi} dBm)", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                        Foreground = rssi >= -67 ? new SolidColorBrush(Color.Parse("#4CC27A")) : rssi >= -75 ? Palette.TextMuted : new SolidColorBrush(Color.Parse("#E0A040"))
                    };
                    rows.Add(LabeledRow("Wi-Fi strength", strength));
                }
                if (net.Snr is int snr) rows.Add(LabeledRow("Signal-to-noise ratio", Info($"{snr} dB")));
                if (net.Frequency is int freq)
                    rows.Add(LabeledRow("Band", Info((freq >= 5000 ? "5 GHz" : "2.4 GHz") + (net.Channel is int ch && ch > 0 ? $", channel {ch}" : ""))));
                if (net.TxRate is int rate) rows.Add(LabeledRow("Transfer rate", Info($"{rate} Mbit/s")));
                if (net.ApMac.Length > 0) rows.Add(LabeledRow("Router (access point) MAC", Selectable(net.ApMac)));
                if (net.FiveGhzOnly is bool fiveOnly)
                    rows.Add(AmpToggle("5 GHz only", fiveOnly, async on =>
                    {
                        if (on && net.Frequency is int f && f < 5000 &&
                            !await Dialogs.ConfirmAsync(this, "The device is connected on 2.4 GHz now. With 5 GHz only it may lose its Wi-Fi connection " +
                                                             "if your router has no 5 GHz network. Continue?", _scale)) return true;
                        return await _owner.SetFiveGhzOnlyAsync(ip, on);
                    }));
                if (net.Roaming is bool roaming)
                    rows.Add(AmpToggle("Wi-Fi roaming (switch to a stronger access point)", roaming, on => _owner.SetWifiRoamingAsync(ip, on)));
            }
            if (net.Ipv6 is bool ipv6) rows.Add(AmpToggle("IPv6", ipv6, on => _owner.SetIpv6Async(ip, on)));
            if (net.PreferredDns is { } dns)
            {
                var box = new TextBox { Width = 160, Text = dns, Watermark = "Automatic" };
                var save = Pill("Set");
                save.Click += async (_, _) =>
                {
                    string value = box.Text?.Trim() ?? string.Empty;
                    if (value.Length > 0 && (!System.Net.IPAddress.TryParse(value, out var address) ||
                                             address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork))
                    {
                        await Dialogs.MessageAsync(this, "Enter an IPv4 address such as 1.1.1.1 or 8.8.8.8, or leave the field empty for automatic.", _scale);
                        return;
                    }
                    await ChangeAmpAsync(() => _owner.SetPreferredDnsAsync(ip, value));
                };
                rows.Add(LabeledRow("Preferred DNS server", new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, Children = { box, save }
                }));
            }
            rows.Add(Muted("Changing these can make the device reconnect to the network for a moment."));
            return Card("Network", rows.ToArray());
        }

        private static string ServerHost(string address) => address.EndsWith(":3483", StringComparison.Ordinal) ? address[..^5] : address;

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
                if (!await Dialogs.ConfirmAsync(this, $"Turn off alarm {alarm.Slot + 1}? This removes it from the device.", _scale)) return;
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
