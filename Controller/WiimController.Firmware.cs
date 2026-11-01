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

sealed record FirmwareCheck(bool Done, bool Failed, string NewVersion)
{
    public bool HasUpdate => NewVersion.Length > 0;
}

sealed partial class WiimController
{
    internal const string FirmwareUpdateAdvice = "Use WiiM Home App firmware update.";
    private static readonly TimeSpan FirmwareCheckTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FirmwareCheckPoll = TimeSpan.FromSeconds(2);

    internal static FirmwareCheck? ParseFirmwareCheck(string? json)
    {
        if (JsonField(json, "ota_state") is not { } state) return null;
        string result = JsonField(json, "ota_result") ?? string.Empty;
        string version = JsonField(json, "new_ver") ?? string.Empty;
        bool failed = state.Contains("fail", StringComparison.OrdinalIgnoreCase) || result.Contains("fail", StringComparison.OrdinalIgnoreCase);
        bool done = failed || state.StartsWith("check_done", StringComparison.OrdinalIgnoreCase);
        bool noUpdate = result.Contains("no_new", StringComparison.OrdinalIgnoreCase) || version is "" or "0";
        return new FirmwareCheck(done, failed, done && !failed && !noUpdate ? version : string.Empty);
    }

    internal async Task<FirmwareCheck?> CheckFirmwareNowAsync(string ip)
    {
        if (!CommandSucceeded(await SendToDeviceAsync(ip, "getMvRemoteUpdateStartCheck").ConfigureAwait(false))) return null;
        var deadline = DateTime.UtcNow + FirmwareCheckTimeout;
        FirmwareCheck? check = null;
        do
        {
            await Task.Delay(FirmwareCheckPoll).ConfigureAwait(false);
            check = ParseFirmwareCheck(await SendToDeviceAsync(ip, "getMVRemoteCheckResult").ConfigureAwait(false));
        }
        while (check is not { Done: true } && DateTime.UtcNow < deadline);
        await Dispatcher.UIThread.InvokeAsync(() => CheckFirmwareAsync(force: true));
        return check is { Done: true } ? check : null;
    }

    private readonly Dictionary<string, FirmwareUpdate> _firmwareUpdates = new();
    private readonly HashSet<string> _firmwareNotified = new();
    private readonly DispatcherTimer _firmwareTimer = new() { Interval = TimeSpan.FromHours(6) };
    private bool _firmwareChecks = true;

    internal bool FirmwareChecks
    {
        get => _firmwareChecks;
        set
        {
            _firmwareChecks = value;
            SaveConfig();
            if (value)
            {
                _ = CheckFirmwareAsync();
                return;
            }
            if (_firmwareUpdates.Count == 0) return;
            _firmwareUpdates.Clear();
            _settingsWindow?.OnFirmwareChecked();
        }
    }

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

    internal async Task CheckFirmwareAsync(bool force = false)
    {
        if (!force && !_firmwareChecks) return;
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
                Notify($"{devices[i].Name}: {update.Summary}. {FirmwareUpdateAdvice}");
        }
        if (changed) _settingsWindow?.OnFirmwareChecked();
    }
}
