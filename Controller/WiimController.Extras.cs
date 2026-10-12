using System.Globalization;
using System.Text.Json;

namespace WiimControl;

sealed partial class WiimController
{
    internal static readonly int[] SleepChoices = [15, 30, 45, 60, 90, 120];
    private const int AlarmSlots = 3;

    private static readonly (string Host, string Name)[] ServiceHosts =
    [
        ("tidal", "TIDAL"), ("qobuz", "Qobuz"), ("deezer", "Deezer"), ("dzcdn", "Deezer"), ("amazon", "Amazon Music"),
        ("scdn.co", "Spotify"), ("spotify", "Spotify"), ("sndcdn", "SoundCloud"), ("soundcloud", "SoundCloud"),
        ("ytimg", "YouTube Music"), ("radiotime", "TuneIn"), ("tunein", "TuneIn"), ("mzstatic", "Apple Music")
    ];

    private readonly Dictionary<string, (DateTime Time, Dictionary<string, string> Names)> _inputRenames = new();

    private static bool ReplyIsOk(string? reply) =>
        reply != null && (reply.Trim() == "OK" || reply.Contains("\"OK\"", StringComparison.Ordinal));

    internal async Task<bool> SetSubwooferAsync(string ip, string key, double value) =>
        ReplyIsOk(await SendToDeviceAsync(ip, $"setSubLPF:{key}:{value.ToString("0.###", CultureInfo.InvariantCulture)}").ConfigureAwait(false));

    internal async Task<int> GetSleepSecondsAsync(string ip) =>
        ParseInt(await SendToDeviceAsync(ip, "getShutdown").ConfigureAwait(false)) is int seconds && seconds > 0 ? seconds : 0;

    internal async Task<bool> SetSleepTimerAsync(string ip, int minutes) =>
        ReplyIsOk(await SendToDeviceAsync(ip, minutes > 0 ? $"setShutdown:{minutes * 60}" : "setShutdown:-1").ConfigureAwait(false));

    internal static (bool Shuffle, int Repeat) ParseLoop(int loop) => loop switch
    {
        0 => (false, 2),
        1 => (false, 1),
        2 => (true, 2),
        3 => (true, 0),
        5 => (true, 1),
        _ => (false, 0)
    };

    internal static int LoopMode(bool shuffle, int repeat) => shuffle
        ? repeat switch { 2 => 2, 1 => 5, _ => 3 }
        : repeat switch { 2 => 0, 1 => 1, _ => 4 };

    internal async Task<bool> SetLoopModeAsync(string ip, int mode) =>
        ReplyIsOk(await SendToDeviceAsync(ip, $"setPlayerCmd:loopmode:{mode}").ConfigureAwait(false));

    internal async Task<bool> SeekAsync(string ip, int seconds) =>
        ReplyIsOk(await SendToDeviceAsync(ip, $"setPlayerCmd:seek:{Math.Max(0, seconds)}").ConfigureAwait(false));

    private static string RenameKey(string input) => input.ToLowerInvariant() switch
    {
        "usbdac" => "pcusb",
        "coaxial" => "co-axial",
        var key => key
    };

    private async Task EnsureInputRenamesAsync(string ip)
    {
        lock (_inputRenames)
            if (_inputRenames.TryGetValue(ip, out var cached) && DateTime.UtcNow - cached.Time < TimeSpan.FromMinutes(5)) return;
        var json = await SendToDeviceAsync(ip, "getModeRename").ConfigureAwait(false);
        if (json == null) return;
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } name)
                        names[RenameKey(p.Name)] = name.Trim();
        }
        catch (JsonException) { }
        lock (_inputRenames) _inputRenames[ip] = (DateTime.UtcNow, names);
    }

    internal string InputName(string ip, string input)
    {
        lock (_inputRenames)
            if (_inputRenames.TryGetValue(ip, out var cached) && cached.Names.TryGetValue(RenameKey(input), out var name)) return name;
        return InputLabel(input);
    }

    private async Task<string> GetBluetoothSourceAsync(string ip)
    {
        var json = await SendToDeviceAsync(ip, "getbtstatus").ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(json ?? string.Empty);
            if (doc.RootElement.TryGetProperty("a2dp_sink", out var sink) &&
                sink.TryGetProperty("link_state", out var state) && state.GetString() == "connected" &&
                sink.TryGetProperty("name", out var name))
                return name.GetString()?.Trim() ?? string.Empty;
        }
        catch (JsonException) { }
        return string.Empty;
    }

    internal static string ServiceName(DeviceStatus status)
    {
        if (SourceName(status.Mode) is { Length: > 0 } source) return source;
        if (Uri.TryCreate(status.ArtUrl, UriKind.Absolute, out var uri))
            foreach (var (host, name) in ServiceHosts)
                if (uri.Host.Contains(host, StringComparison.OrdinalIgnoreCase)) return name;
        return string.Empty;
    }

    internal static string ModelName(string project) => project switch
    {
        _ when project.Contains("Amp_Ultra", StringComparison.OrdinalIgnoreCase) => "WiiM Amp Ultra",
        _ when project.Contains("Amp_Pro", StringComparison.OrdinalIgnoreCase) => "WiiM Amp Pro",
        _ when project.Contains("WiiM_Amp", StringComparison.OrdinalIgnoreCase) => "WiiM Amp",
        _ => project.Replace('_', ' ')
    };

    private static DeviceInfo? ParseDeviceInfo(string? statusEx)
    {
        try
        {
            using var doc = JsonDocument.Parse(statusEx ?? string.Empty);
            var root = doc.RootElement;
            string Field(string name) => root.TryGetProperty(name, out var p) ? p.ToString() : string.Empty;
            int? Temperature(string name) => int.TryParse(Field(name), out int t) && t is > -40 and < 150 ? t : null;
            string firmware = Field("firmware");
            if (DateTime.TryParseExact(Field("Release"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var released))
                firmware += $" ({released:d})";
            return new DeviceInfo(ModelName(Field("project")), firmware, Temperature("temperature_cpu"), Temperature("temperature_tmp102"));
        }
        catch (JsonException) { return null; }
    }

    internal async Task<List<AlarmInfo>?> GetAlarmsAsync(string ip)
    {
        var replies = await Task.WhenAll(Enumerable.Range(0, AlarmSlots).Select(n => SendToDeviceAsync(ip, $"getAlarmClock:{n}"))).ConfigureAwait(false);
        var alarms = new List<AlarmInfo>();
        for (int slot = 0; slot < AlarmSlots; slot++)
        {
            try
            {
                using var doc = JsonDocument.Parse(replies[slot] ?? string.Empty);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("enable", out var enable)) return null;
                string Field(string name) => root.TryGetProperty(name, out var p) ? p.ToString() : string.Empty;
                TimeSpan? time = TimeSpan.TryParseExact(Field("time"), @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var t) ? t : null;
                string path = Field("path") is "null" or "(null)" ? string.Empty : Field("path");
                alarms.Add(new AlarmInfo(slot, enable.ToString() == "1", Field("trigger"), time, path));
            }
            catch (JsonException) { return null; }
        }
        return alarms;
    }

    internal static TimeSpan UtcToLocalTime(TimeSpan utc) => (DateTime.SpecifyKind(DateTime.UtcNow.Date + utc, DateTimeKind.Utc)).ToLocalTime().TimeOfDay;

    internal async Task<bool> SetDailyAlarmAsync(string ip, int slot, TimeSpan localTime, string url)
    {
        var utc = (DateTime.Today + localTime).ToUniversalTime().TimeOfDay;
        string time = string.Create(CultureInfo.InvariantCulture, $"{utc.Hours:00}{utc.Minutes:00}00");
        if (!ReplyIsOk(await SendToDeviceAsync(ip, $"setAlarmClock:{slot}:2:1:{time}:{url}").ConfigureAwait(false))) return false;
        var alarms = await GetAlarmsAsync(ip).ConfigureAwait(false);
        return alarms != null && alarms[slot].Enabled && alarms[slot].Path == url;
    }

    internal async Task<bool> ClearAlarmAsync(string ip, int slot)
    {
        if (!ReplyIsOk(await SendToDeviceAsync(ip, $"setAlarmClock:{slot}:0").ConfigureAwait(false))) return false;
        var alarms = await GetAlarmsAsync(ip).ConfigureAwait(false);
        return alarms != null && !alarms[slot].Enabled;
    }
}
