using Avalonia.Controls;
using Avalonia.Layout;

namespace WiimControl;

sealed partial class WiimController
{
    private sealed partial class SettingsWindow
    {
        private readonly ComboBox _cboHtFront = new() { Width = 240 };
        private readonly StackPanel _htRows = new();
        private Border? _htCard;
        private List<HomeTheaterDevice> _htDevices = [];
        private bool _htFilling;
        private bool _htBusy;
        private int _htVersion;

        private Border HomeTheaterCard()
        {
            if (_htCard != null) return _htCard;
            ToolTip.SetTip(_cboHtFront, "The WiiM device connected to your TV");
            _cboHtFront.SelectionChanged += async (_, _) => { if (!_htFilling) await RefreshHomeTheaterAsync(reloadDevices: false); };
            _htCard = Card("Home theater",
                Muted("Use other WiiM devices as the center and surround speakers of a WiiM Ultra, Amp Pro, Amp Ultra or Bar that's connected to your TV. " +
                      "The WiiM Bar plays the center itself, so it only takes surround speakers."),
                LabeledRow("Front device", _cboHtFront),
                _htRows);
            return _htCard;
        }

        private async Task RefreshHomeTheaterAsync(bool reloadDevices = true)
        {
            if (_htBusy || _owner._knownDevices.Count < 2) return;
            int version = ++_htVersion;
            if (reloadDevices || _htDevices.Count == 0)
            {
                if (_htRows.Children.Count == 0) SetRows(_htRows, [Muted("Loading…")]);
                var devices = await _owner.GetHomeTheaterDevicesAsync();
                if (version != _htVersion) return;
                _htDevices = devices;
                string? chosen = (_cboHtFront.SelectedItem as ComboBoxItem)?.Tag as string;
                var fronts = devices.Where(d => d.CanBeFront).ToList();
                _htFilling = true;
                _cboHtFront.Items.Clear();
                foreach (var d in fronts) _cboHtFront.Items.Add(ComboItem(d.Ip, d.Name));
                int index = fronts.FindIndex(d => d.Ip == chosen);
                if (index < 0) index = fronts.FindIndex(d => d.Ip == _owner._deviceIp);
                _cboHtFront.SelectedIndex = index >= 0 ? index : fronts.Count > 0 ? 0 : -1;
                _htFilling = false;
            }
            if ((_cboHtFront.SelectedItem as ComboBoxItem)?.Tag is not string frontIp)
            {
                SetRows(_htRows, [Muted("None of your devices can be the front of a home theater. That needs a WiiM Ultra, Amp Pro, Amp Ultra or Bar.")]);
                return;
            }
            var state = await _owner.GetHomeTheaterAsync(frontIp, _htDevices);
            if (version != _htVersion) return;
            SetRows(_htRows, BuildHomeTheaterRows(frontIp, state));
        }

        private List<Control> BuildHomeTheaterRows(string frontIp, HomeTheaterState state)
        {
            if (!state.Supported) return [Muted("This device didn't report its home theater settings. Check that it's switched on and reachable.")];
            var others = state.Devices.Where(d => d.Ip != frontIp).ToList();
            bool soundbar = IsSoundbar(state.Devices.FirstOrDefault(d => d.Ip == frontIp)?.Project);
            if (others.Count == 0) return [Muted("Add another WiiM device on the Devices page to use it as a center or surround speaker.")];

            string? Member(params int[] channels) => state.Members.FirstOrDefault(m => channels.Contains(m.Channel))?.Uuid;
            string? center = Member(CenterChannel);
            string? both = Member(SurroundBothChannel);
            string? left = both ?? Member(SurroundLeftChannel);
            string? right = both ?? Member(SurroundRightChannel);

            ComboBox Picker(string? selected, List<HomeTheaterDevice> choices)
            {
                var combo = new ComboBox { Width = FieldWidth };
                combo.Items.Add(ComboItem(string.Empty, "None"));
                foreach (var d in choices) combo.Items.Add(ComboItem(d.Uuid, d.Name));
                int index = choices.FindIndex(d => d.Uuid.Equals(selected, StringComparison.OrdinalIgnoreCase));
                combo.SelectedIndex = index >= 0 ? index + 1 : 0;
                return combo;
            }
            var centerBox = Picker(center, others.Where(d => !IsSoundbar(d.Project)).ToList());
            var leftBox = Picker(left, others);
            var rightBox = Picker(right, others);
            var surroundLeftRow = LabeledRow("Surround left", leftBox);
            ToolTip.SetTip(surroundLeftRow, "Pick the same device for left and right to let one device play both surround channels. A WiiM Sound plays one side only.");
            var surroundRightRow = LabeledRow("Surround right", rightBox);
            ToolTip.SetTip(surroundRightRow, "Pick the same device for left and right to let one device play both surround channels. A WiiM Sound plays one side only.");

            string status = state.Members.Count == 0 ? "Not set up" : state.Active ? "Active" : "Set up, not active";
            var rows = new List<Control>
            {
                LabeledRow("Status", Info(status)),
                surroundLeftRow,
                surroundRightRow
            };
            if (!soundbar) rows.Insert(1, LabeledRow("Center", centerBox));

            var apply = Pill("Apply", accent: true);
            apply.Click += async (_, _) =>
            {
                string Uuid(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
                HomeTheaterDevice? Device(string uuid) => others.FirstOrDefault(d => d.Uuid == uuid);
                string c = Uuid(centerBox), l = Uuid(leftBox), r = Uuid(rightBox);
                var members = new List<(HomeTheaterDevice, int)>();
                if (!soundbar && Device(c) is { } centerDevice) members.Add((centerDevice, CenterChannel));
                if (l.Length > 0 && l == r) members.Add((Device(l)!, SurroundBothChannel));
                else
                {
                    if (Device(l) is { } leftDevice) members.Add((leftDevice, SurroundLeftChannel));
                    if (Device(r) is { } rightDevice) members.Add((rightDevice, SurroundRightChannel));
                }
                if (l.Length > 0 && l == r && Device(l) is { } single && IsSingleSpeaker(single.Project))
                {
                    await Dialogs.MessageAsync(this,
                        $"{single.Name} is a single speaker, so it can play surround left or right, but not both. Pick another device for the other side.", _scale);
                    return;
                }
                if (members.Select(m => m.Item1.Uuid).Distinct().Count() != members.Count)
                {
                    await Dialogs.MessageAsync(this, "A device can only have one role. Pick different devices for the center and the surrounds.", _scale);
                    return;
                }
                await ChangeHomeTheaterAsync(frontIp, members);
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { apply } };
            if (state.Members.Count > 0)
            {
                var remove = Pill("Remove");
                ToolTip.SetTip(remove, "Stop using the center and surround speakers. The devices play on their own again.");
                remove.Click += async (_, _) =>
                {
                    if (await Dialogs.ConfirmAsync(this, "Remove the home theater? The center and surround devices play on their own again.", _scale))
                        await ChangeHomeTheaterAsync(frontIp, []);
                };
                buttons.Children.Insert(0, remove);
            }
            rows.Add(buttons);
            rows.Add(Muted("Applying takes a few seconds. The center and surround devices stop what they're playing and follow the front device."));
            return rows;
        }

        private async Task ChangeHomeTheaterAsync(string frontIp, List<(HomeTheaterDevice, int)> members)
        {
            if (_htBusy) return;
            _htBusy = true;
            SetRows(_htRows, [Muted(members.Count == 0 ? "Removing the home theater…" : "Setting up the home theater…")]);
            bool ok;
            try { ok = await _owner.SetHomeTheaterAsync(frontIp, members); }
            finally { _htBusy = false; }
            if (!ok)
                await Dialogs.MessageAsync(this, members.Count == 0
                    ? "The device didn't remove the home theater. Try again, or check it in the WiiM Home app."
                    : "The home theater wasn't set up completely. Check that all devices are switched on, then try again.", _scale);
            await RefreshMultiroomAsync();
        }
    }
}
