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

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Fetched, int Seconds)> _sleepCache = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Fetched, int Mode)> _lastModes = new();

    internal async Task<int> GetSleepSecondsAsync(string ip)
    {
        var now = DateTime.UtcNow;
        if (_sleepCache.TryGetValue(ip, out var cached) && now - cached.Fetched < TimeSpan.FromSeconds(30))
            return cached.Seconds > 0 ? Math.Max(0, cached.Seconds - (int)(now - cached.Fetched).TotalSeconds) : 0;
        int seconds = ParseInt(await SendToDeviceAsync(ip, "getShutdown").ConfigureAwait(false)) is int s && s > 0 ? s : 0;
        _sleepCache[ip] = (now, seconds);
        return seconds;
    }

    internal async Task<bool> SetSleepTimerAsync(string ip, int minutes)
    {
        _sleepCache.TryRemove(ip, out _);
        return ReplyIsOk(await SendToDeviceAsync(ip, minutes > 0 ? $"setShutdown:{minutes * 60}" : "setShutdown:-1").ConfigureAwait(false));
    }

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
        await TryRoonLoopAsync(ip, mode).ConfigureAwait(false) ||
        ReplyIsOk(await SendToDeviceAsync(ip, $"setPlayerCmd:loopmode:{mode}").ConfigureAwait(false));

    internal async Task<bool> SeekAsync(string ip, int seconds) =>
        await TryExternalCommandAsync(ip, "seek", seconds).ConfigureAwait(false) ||
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

    private const int AlexaMode = 3;

    internal static string ServiceName(DeviceStatus status)
    {
        if (status.SourceLabel.Length > 0) return status.SourceLabel;
        string fromArt = string.Empty;
        if (Uri.TryCreate(status.ArtUrl, UriKind.Absolute, out var uri))
            fromArt = ServiceHosts.FirstOrDefault(s => uri.Host.Contains(s.Host, StringComparison.OrdinalIgnoreCase)).Name ?? string.Empty;
        string fromVendor = VendorName(status.Vendor);
        string service = fromVendor.Length > 0 ? fromVendor : fromArt;
        if (status.Mode == AlexaMode && service.Length > 0) return $"Alexa · {service}";
        if (SourceName(status.Mode) is { Length: > 0 } source) return source;
        return service;
    }

    internal static string VendorName(string vendor)
    {
        string text = vendor.Trim();
        if (text.Length == 0 || text.Length > 40 || text.Contains(':') || text.Contains('/') || !text.Any(char.IsLetter)) return string.Empty;
        string key = text.Replace(" ", string.Empty).Replace("_", string.Empty);
        if (ServiceHosts.FirstOrDefault(s => key.Contains(s.Host.Replace(".", string.Empty), StringComparison.OrdinalIgnoreCase) ||
                                             key.Contains(s.Name.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase)).Name is { } known)
            return known;
        return text;
    }

    private static readonly Dictionary<string, string> ModelNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WiiM_Amp_4layer"] = "WiiM Amp",
        ["WiiM_AMP"] = "WiiM Amp",
        ["WiiM_Amp_Pro"] = "WiiM Amp Pro",
        ["WiiM_Amp_Ultra"] = "WiiM Amp Ultra",
        ["WiiM_Pro_with_gc4a"] = "WiiM Pro",
        ["WiiM_Pro_Plus"] = "WiiM Pro Plus",
        ["Muzo_Mini"] = "WiiM Mini",
        ["WiiM_Mini"] = "WiiM Mini",
        ["WiiM_Ultra"] = "WiiM Ultra",
        ["WiiM_Sound"] = "WiiM Sound",
        ["WiiM_CI_MOD_S"] = "WiiM CI MOD S",
        ["WiiM_CI_MOD_A80"] = "WiiM CI MOD A80"
    };

    private static readonly string[] SubwooferProjects = ["WiiM_Amp", "WiiM_AMP", "WiiM_Ultra", "WiiM_CI_AMP", "WiiM_CI_MOD_A80"];

    internal static bool HasSubwooferOutput(string? project) =>
        project is { Length: > 0 } && SubwooferProjects.Any(p => project.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] ProjectSuffixes = ["_4layer", "_with_gc4a"];

    internal static bool IsKnownProject(string project) => ModelNames.ContainsKey(project);

    internal static string CleanMac(string mac)
    {
        string text = mac.Trim().ToUpperInvariant();
        return text.Length == 0 || text.All(c => c is '0' or ':' or '-') ? string.Empty : text;
    }

    private static readonly string[] SilverProjects = ["WiiM_Amp", "WiiM_Ultra"];
    private static readonly string[] WhiteProjects = ["WiiM_Sound", "WiiM_SoundLite", "WiiM_Sub"];

    internal static string ColourName(string project, int? number, string text)
    {
        if (text.Trim().Length > 0) return text.Trim();
        if (number is not int n) return string.Empty;
        if (SilverProjects.Any(p => project.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return n == 2 ? "Silver" : "Space grey";
        if (WhiteProjects.Any(p => project.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return n == 1 ? "White" : "Black";
        return string.Empty;
    }

    private static readonly (string Field, string Part)[] ComponentFields = [("mcu_ver", "MCU"), ("dsp_ver", "DSP"), ("hdmi_ver", "HDMI")];

    internal static List<(string Part, string Version)> ComponentVersions(Func<string, string> field) =>
        ComponentFields.Select(c => (c.Part, Version: field(c.Field).Trim())).Where(c => c.Version.Length > 0 && c.Version != "0").ToList();

    internal static string ModelName(string project)
    {
        if (ModelNames.TryGetValue(project, out var name)) return name;
        string trimmed = project;
        foreach (var suffix in ProjectSuffixes)
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[..^suffix.Length];
        if (trimmed.StartsWith("WiiM_", StringComparison.OrdinalIgnoreCase)) trimmed = "WiiM_" + trimmed[5..];
        return trimmed.Replace('_', ' ').Trim();
    }

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
            return new DeviceInfo(ModelName(Field("project")), firmware, Temperature("temperature_cpu"), Temperature("temperature_tmp102"),
                ParseFirmwareUpdate(statusEx), Field("ssid"), Field("MAC"), Field("uuid"), Field("project"),
                int.TryParse(Field("ModuleColorNumber"), out int color) ? color : null,
                ColourName(Field("project"), int.TryParse(Field("ModuleColorNumber"), out int colour) ? colour : null, Field("ModuleColorString")),
                ComponentVersions(Field), CleanMac(Field("ETH_MAC")), CleanMac(Field("BTMAC") is { Length: > 0 } bt ? bt : Field("BT_MAC")));
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
