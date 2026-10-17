using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private async Task ShowSqueezeliteAsync()
        {
            if (AmpOf(_cboLibraryAmp) is not { } ip)
            {
                await Dialogs.MessageAsync(this, "Add a WiiM device on the Devices page first.", _scale);
                return;
            }
            var heading = new TextBlock { Text = $"Squeezelite on {DeviceName(ip)}", Classes = { "heading" } };
            var body = new StackPanel { Spacing = 10, Width = 520 };
            bool busy = false;

            async Task Run(Func<Task<bool>> action, int settleMs)
            {
                if (busy) return;
                busy = true;
                SetRows(body, [heading, Muted("Working…")]);
                bool ok = await action();
                if (settleMs > 0) await Task.Delay(settleMs);
                busy = false;
                if (!ok) await Dialogs.MessageAsync(this, "The device didn't accept the request.", _scale);
                await Reload();
            }

            async Task Reload()
            {
                if (body.Children.Count <= 1) SetRows(body, [heading, Muted("Loading…")]);
                var state = await _owner.GetSqueezeliteStateAsync(ip);
                if (state == null)
                {
                    SetRows(body, [heading, Muted("This device didn't report Squeezelite. Check that it's reachable and that Squeezelite is switched on for it in the WiiM Home app.")]);
                    return;
                }
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
                search.Click += async (_, _) => await Run(() => _owner.DiscoverSqueezeliteAsync(ip), 3000);
                var connect = Pill("Connect");
                connect.IsEnabled = servers.Count > 0;
                connect.Click += async (_, _) =>
                {
                    if ((server.SelectedItem as ComboBoxItem)?.Tag is string address)
                        await Run(() => _owner.ConnectSqueezeliteAsync(ip, address), 2500);
                };
                var auto = new ToggleSwitch { IsChecked = state.AutoConnect };
                auto.IsCheckedChanged += async (_, _) => await Run(() => _owner.SetSqueezeliteAutoConnectAsync(ip, auto.IsChecked == true), 0);

                SetRows(body,
                [
                    heading,
                    new TextBlock
                    {
                        Text = "Squeezelite lets the device play from a Lyrion Music Server. It's switched on or off in the WiiM Home app.",
                        TextWrapping = TextWrapping.Wrap, Classes = { "muted" }
                    },
                    LabeledRow("Status", Info(status)),
                    LabeledRow("Server", new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { search, connect, server }
                    }),
                    ToggleRow("Connect automatically", auto)
                ]);
            }

            _ = Reload();
            await Dialogs.ContentAsync(this, body, _scale);
        }
    }
}
