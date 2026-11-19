using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private sealed partial class AmpPage
        {
            public const string SectionRemote = "Remote";

            private List<Control> BuildRemoteCards(string ip, AmpSettings s)
            {
                var cards = new List<Control>();
                if (s.Remote is { } remote) cards.Add(BuildRemoteCard(ip, remote));
                var steps = new List<Control>();
                if (s.RemoteVolumeStep is int remoteStep)
                {
                    var slider = AmpSlider(1, 10, Math.Clamp(remoteStep, 1, 10), v => v.ToString(CultureInfo.CurrentCulture), false, out var row);
                    slider.ValueCommitted += async (_, _) => await ChangeAmpAsync(() => window._owner.SetRemoteVolumeStepAsync(ip, slider.Value));
                    var label = LabeledRow("Volume step for the remote", row);
                    ToolTip.SetTip(label, "How much the volume changes per press of the volume buttons on the WiiM remote");
                    steps.Add(label);
                }
                if (s.TvRemoteVolumeStep is int tvStep)
                {
                    var slider = AmpSlider(1, 10, Math.Clamp(tvStep, 1, 10), v => v.ToString(CultureInfo.CurrentCulture), false, out var row);
                    slider.ValueCommitted += async (_, _) => await ChangeAmpAsync(() => window._owner.SetTvRemoteVolumeStepAsync(ip, slider.Value));
                    var label = LabeledRow("Volume step for the TV remote", row);
                    ToolTip.SetTip(label, "How much the volume changes per press of the TV remote's volume buttons, when the TV is connected with HDMI");
                    steps.Add(label);
                }
                if (steps.Count > 0) cards.Add(Card("Volume steps", steps.ToArray()));
                if (cards.Count == 0) cards.Add(Muted("This device doesn't work with the WiiM remote."));
                return cards;
            }

            private Border BuildRemoteCard(string ip, RemoteInfo remote)
            {
                var rows = new List<Control>();
                if (remote.Connected)
                {
                    rows.Add(LabeledRow("Remote", Info(remote.Name.Length > 0 ? remote.Name : "WiiM remote")));
                    if (remote.Battery is int battery) rows.Add(LabeledRow("Battery", Info(battery <= 15 ? $"{battery}% · charge or replace soon" : $"{battery}%")));
                    if (remote.Signal is int signal)
                        rows.Add(LabeledRow("Signal", Info($"{(signal >= -60 ? "Strong" : signal >= -75 ? "Good" : "Weak")} ({signal} dBm)")));
                    if (remote.Version.Length > 0) rows.Add(LabeledRow("Firmware", Info(remote.Version)));
                    var forget = Pill("Forget remote");
                    ToolTip.SetTip(forget, "Unpair the remote from this device");
                    forget.Click += async (_, _) =>
                    {
                        if (!await Dialogs.ConfirmAsync(window, $"Forget the remote? It stops controlling {window.DeviceName(ip)} until you pair it again.", window._scale)) return;
                        await ChangeAmpAsync(() => window._owner.ForgetRemoteAsync(ip));
                    };
                    forget.HorizontalAlignment = HorizontalAlignment.Left;
                    forget.Margin = new Avalonia.Thickness(0, 8, 0, 0);
                    rows.Add(forget);
                }
                else
                {
                    rows.Add(Muted("No WiiM remote is paired with this device."));
                    var pair = Pill("Pair a remote", accent: true);
                    pair.HorizontalAlignment = HorizontalAlignment.Left;
                    pair.Click += async (_, _) => await PairRemoteAsync(ip, pair);
                    rows.Add(pair);
                }
                return Card("WiiM remote", rows.ToArray());
            }

            private async Task PairRemoteAsync(string ip, Button pair)
            {
                if (!await Dialogs.ConfirmAsync(window,
                        "Keep the remote close to the device and put it into pairing mode: press and hold the Input and Mute buttons at the same time for 2 seconds. Click OK when it's done.",
                        window._scale)) return;
                pair.IsEnabled = false;
                pair.Content = "Looking for the remote…";
                window._lblAmpStatus.Text = "Pairing…";
                var result = await window._owner.PairRemoteAsync(ip);
                pair.IsEnabled = true;
                pair.Content = "Pair a remote";
                window._lblAmpStatus.Text = string.Empty;
                await Dialogs.MessageAsync(window, result switch
                {
                    RemotePairResult.Paired => "The remote is paired.",
                    RemotePairResult.NotFound =>
                        "No remote was found. Make sure it's powered on, close to the device and in pairing mode, then try again.",
                    _ => "The remote didn't pair. Check its battery level, then try again."
                }, window._scale);
                await RefreshAmpAsync();
            }
        }
    }
}
