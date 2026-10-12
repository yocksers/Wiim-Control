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
            return Card("Device", rows.ToArray());
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

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(time);
            Grid.SetColumn(url, 1);
            row.Children.Add(url);
            Grid.SetColumn(save, 2);
            row.Children.Add(save);
            Grid.SetColumn(remove, 3);
            row.Children.Add(remove);
            return new StackPanel { Margin = new Thickness(0, 8, 0, 4), Children = { heading, row } };
        }
    }
}
