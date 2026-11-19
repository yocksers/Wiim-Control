using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed class StreamingServicesPage(SettingsWindow window)
        {
            private const int SettingChecks = 6;
            private static readonly TimeSpan SettingCheckInterval = TimeSpan.FromMilliseconds(700);

            private readonly TextBlock _status = Subtitle();
            private readonly ComboBox _cboAmp = new() { Width = 200 };
            private readonly StackPanel _body = new() { Spacing = 16 };
            private int _version;

            public void FillDevices() => window.FillAmpCombo(_cboAmp);

            public Control Build()
            {
                ToolTip.SetTip(_cboAmp, "The device to change");
                _cboAmp.SelectionChanged += async (_, _) => { if (!window._loading) await LoadAsync(); };
                var page = window.Page("Streaming Services", _status, [], _body, subtitleBelow: true, secondRow: [_cboAmp]);
                page.PropertyChanged += async (_, e) =>
                {
                    if (e.Property == IsVisibleProperty && page.IsVisible) await LoadAsync();
                };
                return page;
            }

            private async Task LoadAsync()
            {
                int version = ++_version;
                if (AmpOf(_cboAmp) is not { } ip)
                {
                    _body.Children.Clear();
                    _body.Children.Add(Muted("Add a WiiM device on the Devices page first."));
                    return;
                }
                _status.Text = "Loading…";
                var servicesTask = window._owner.GetStreamServicesAsync(ip);
                var castTask = window._owner.GetGoogleCastAsync(ip);
                await Task.WhenAll(servicesTask, castTask);
                var services = servicesTask.Result;
                bool squeezeliteOn = services?.Any(s => s.Id == SqueezeliteService && s.Active) == true;
                var squeezelite = squeezeliteOn ? await window._owner.GetSqueezeliteStateAsync(ip) : null;
                if (version != _version) return;
                _status.Text = string.Empty;
                _body.Children.Clear();
                if (services == null && castTask.Result == null)
                {
                    _body.Children.Add(Muted("The device didn't answer. Check that it's switched on and reachable."));
                    return;
                }
                var rows = new List<Control>();
                foreach (var (id, name, active) in services ?? [])
                {
                    string service = id;
                    rows.Add(SettingToggle(name, active, on => window._owner.SetStreamServiceActiveAsync(ip, service, on),
                        () => window._owner.GetStreamServiceActiveAsync(ip, service), reload: service == SqueezeliteService));
                    if (service == SqueezeliteService && squeezelite != null)
                    {
                        var reconnect = SettingToggle("Reconnect automatically", squeezelite.AutoConnect, on => window._owner.SetSqueezeliteAutoConnectAsync(ip, on),
                            async () => (await window._owner.GetSqueezeliteStateAsync(ip))?.AutoConnect);
                        reconnect.Margin = new Thickness(24, 0, 0, 0);
                        rows.Add(reconnect);
                    }
                }
                if (rows.Count == 0) rows.Add(Muted("This device has no streaming services that can be switched on or off."));
                _body.Children.Add(Card("Streaming services", rows.ToArray()));
                if (castTask.Result is bool cast)
                    _body.Children.Add(Card("Google Cast",
                        SettingToggle("Google Cast", cast, on => window._owner.SetGoogleCastAsync(ip, on), () => window._owner.GetGoogleCastAsync(ip))));
                if (services is { Count: > 0 } && window._owner._knownDevices.Count > 1)
                {
                    var copy = Pill("Use these settings on all devices");
                    copy.HorizontalAlignment = HorizontalAlignment.Left;
                    ToolTip.SetTip(copy, "Switch the same streaming services on and off on your other devices");
                    copy.Click += async (_, _) => await CopyToAllAsync(ip, copy);
                    _body.Children.Add(copy);
                }
            }

            private Grid SettingToggle(string text, bool value, Func<bool, Task<bool>> apply, Func<Task<bool?>> read, bool reload = false)
            {
                var toggle = new ToggleSwitch { IsChecked = value };
                bool reverting = false;
                toggle.IsCheckedChanged += async (_, _) =>
                {
                    if (reverting) return;
                    bool wanted = toggle.IsChecked == true;
                    toggle.IsEnabled = false;
                    bool ok = await apply(wanted) && await ConfirmedAsync(read, wanted);
                    toggle.IsEnabled = true;
                    if (!ok)
                    {
                        reverting = true;
                        toggle.IsChecked = !wanted;
                        reverting = false;
                        await Dialogs.MessageAsync(window, "The device didn't accept the change.", window._scale);
                        return;
                    }
                    if (reload) await LoadAsync();
                };
                return ToggleRow(text, toggle);
            }

            private static async Task<bool> ConfirmedAsync(Func<Task<bool?>> read, bool wanted)
            {
                for (int i = 0; i < SettingChecks; i++)
                {
                    if (await read() == wanted) return true;
                    await Task.Delay(SettingCheckInterval);
                }
                return false;
            }

            private async Task CopyToAllAsync(string ip, Button copy)
            {
                var others = window._owner._knownDevices.Where(d => d.Ip != ip).ToList();
                if (others.Count == 0 || !await Dialogs.ConfirmAsync(window,
                        $"Switch the streaming services on and off on {(others.Count == 1 ? others[0].Name : $"your {others.Count} other devices")} the same way as on {window.DeviceName(ip)}?",
                        window._scale)) return;
                copy.IsEnabled = false;
                _status.Text = "Copying the settings…";
                var wanted = await window._owner.GetStreamServicesAsync(ip);
                var failed = new List<string>();
                foreach (var device in others)
                {
                    var theirs = await window._owner.GetStreamServicesAsync(device.Ip);
                    if (theirs == null || wanted == null)
                    {
                        failed.Add(device.Name);
                        continue;
                    }
                    foreach (var service in wanted)
                        if (theirs.FirstOrDefault(t => t.Id == service.Id) is { Id: not null } current && current.Active != service.Active &&
                            !await window._owner.SetStreamServiceActiveAsync(device.Ip, service.Id, service.Active))
                        {
                            failed.Add(device.Name);
                            break;
                        }
                }
                copy.IsEnabled = true;
                _status.Text = string.Empty;
                await Dialogs.MessageAsync(window, failed.Count == 0
                    ? "The other devices now use the same streaming services."
                    : $"These devices didn't accept all the changes: {string.Join(", ", failed)}.", window._scale);
            }
        }
    }
}
