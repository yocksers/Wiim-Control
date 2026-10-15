using Avalonia.Threading;

namespace WiimControl;

sealed record FirmwareUpdate(string Current, string Available, string RemoteAvailable)
{
    public bool HasAmpUpdate => Available.Length > 0;
    public bool HasRemoteUpdate => RemoteAvailable.Length > 0;

    public string Summary => HasAmpUpdate && HasRemoteUpdate
        ? $"Firmware {ShortVersion(Available)} and a remote update are available"
        : HasAmpUpdate ? $"Firmware {ShortVersion(Available)} is available" : "A remote control update is available";

    public static string ShortVersion(string version) =>
        version.StartsWith("Linkplay.", StringComparison.OrdinalIgnoreCase) ? version[9..] : version;
}

sealed partial class WiimController
{
    private readonly Dictionary<string, FirmwareUpdate> _firmwareUpdates = new();
    private readonly HashSet<string> _firmwareNotified = new();
    private readonly DispatcherTimer _firmwareTimer = new() { Interval = TimeSpan.FromHours(6) };

    internal FirmwareUpdate? FirmwareUpdateFor(string ip) => _firmwareUpdates.GetValueOrDefault(ip);

    internal static FirmwareUpdate? ParseFirmwareUpdate(string? statusEx)
    {
        if (statusEx == null) return null;
        string current = JsonField(statusEx, "firmware") ?? string.Empty;
        string newVersion = JsonField(statusEx, "NewVer") ?? string.Empty;
        bool amp = JsonField(statusEx, "VersionUpdate") == "1" && newVersion.Length > 1 && newVersion != current;
        bool remote = JsonField(statusEx, "BleRemote_has_new") == "1";
        string remoteVersion = JsonField(statusEx, "BleRemoteNewVersion") ?? string.Empty;
        if (!amp && !remote) return null;
        return new FirmwareUpdate(current, amp ? newVersion : string.Empty, remote ? (remoteVersion.Length > 0 ? remoteVersion : "new") : string.Empty);
    }

    private void StartFirmwareChecks()
    {
        _firmwareTimer.Tick += async (_, _) => await CheckFirmwareAsync();
        _firmwareTimer.Start();
        DispatcherTimer.RunOnce(() => _ = CheckFirmwareAsync(), TimeSpan.FromSeconds(20));
    }

    internal async Task CheckFirmwareAsync()
    {
        var devices = _knownDevices.ToList();
        var replies = await Task.WhenAll(devices.Select(d => SendToDeviceAsync(d.Ip, "getStatusEx")));
        bool changed = false;
        for (int i = 0; i < devices.Count; i++)
        {
            if (replies[i] == null) continue;
            var update = ParseFirmwareUpdate(replies[i]);
            var ip = devices[i].Ip;
            if (update == null)
            {
                changed |= _firmwareUpdates.Remove(ip);
                continue;
            }
            changed |= !_firmwareUpdates.TryGetValue(ip, out var old) || old != update;
            _firmwareUpdates[ip] = update;
            string key = $"{devices[i].Uuid}|{ip}|{update.Available}|{update.RemoteAvailable}";
            if (_firmwareNotified.Add(key))
                Notify($"{devices[i].Name}: {update.Summary}. Install it from the WiiM Home app.");
        }
        if (changed) _settingsWindow?.OnFirmwareChecked();
    }
}
