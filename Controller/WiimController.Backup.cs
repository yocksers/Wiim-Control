using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WiimControl;

sealed record BandValue(string Param, double Value);

sealed record EqPresetBackup(string Name, string ChannelMode, Dictionary<string, List<BandValue>> Bands);

sealed record AmpBackup(string Name, string Ip, string Uuid, List<EqPresetBackup> GraphicEq, List<EqPresetBackup> ParametricEq,
    List<EqPresetBackup> RoomFit)
{
    [JsonIgnore] public int Count => GraphicEq.Count + ParametricEq.Count + RoomFit.Count;
}

sealed record BackupFile(string App, string Version, DateTime Created, string Settings, List<AmpBackup> Amps);

sealed record RestoreResult(int Restored, int Failed);

sealed partial class WiimController
{
    private const string BackupApp = "Wiim Control";
    private bool _configLocked;
    private static readonly string[] BandKeys = ["EQBand", "EQBandL", "EQBandR"];

    internal static readonly JsonSerializerOptions BackupJson = new() { WriteIndented = true };

    private sealed record SourceBands(string Name, bool Enabled, string ChannelMode, Dictionary<string, List<BandValue>> Bands);

    internal string ReadConfigText()
    {
        try { return File.Exists(_cfgPath) ? File.ReadAllText(_cfgPath) : string.Empty; }
        catch (Exception) { return string.Empty; }
    }

    internal BackupFile CreateBackupFile(List<AmpBackup> amps) =>
        new(BackupApp, AppVersion, DateTime.Now, ReadConfigText(), amps);

    internal static BackupFile? ParseBackupFile(string json)
    {
        try
        {
            var backup = JsonSerializer.Deserialize<BackupFile>(json, BackupJson);
            return backup is { App: BackupApp, Amps: not null } ? backup : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<SourceBands?> ReadBandsAsync(string ip, object payload)
    {
        var json = await SendEqJsonAsync(ip, "EQGetLV2SourceBandEx", payload).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(json ?? string.Empty);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var bands = new Dictionary<string, List<BandValue>>();
            foreach (var key in BandKeys)
                if (root.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array)
                    bands[key] = array.EnumerateArray()
                        .Where(b => b.TryGetProperty("param_name", out _) && b.TryGetProperty("value", out _))
                        .Select(b => new BandValue(b.GetProperty("param_name").GetString() ?? string.Empty, ReadNumber(b.GetProperty("value"))))
                        .ToList();
            if (bands.Count == 0) return null;
            string Str(string name) => root.TryGetProperty(name, out var p) ? p.ToString() : string.Empty;
            return new SourceBands(Str("Name"), Str("EQStat") == "On", Str("channelMode") is { Length: > 0 } mode ? mode : "Stereo", bands);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private Task<string?> WriteBandsAsync(string ip, Dictionary<string, object> target, string channelMode, Dictionary<string, List<BandValue>> bands)
    {
        var payload = new Dictionary<string, object>(target) { ["channelMode"] = channelMode };
        foreach (var (key, values) in bands)
            payload[key] = values.Select(b => new { param_name = b.Param, value = Math.Round(b.Value, 3) }).ToList();
        return SendEqJsonAsync(ip, "EQSetLV2SourceBand", payload);
    }

    private static string? PickSpareSource(List<EqSourceMode> modes, string current)
    {
        var others = modes.Where(m => !m.Source.Equals(current, StringComparison.OrdinalIgnoreCase)).ToList();
        return (others.FirstOrDefault(m => !m.Enabled) ?? others.FirstOrDefault())?.Source;
    }

    private async Task<(string Source, EqSourceMode Mode)?> FindSpareSourceAsync(string ip)
    {
        var modesTask = GetEqSourceModesAsync(ip);
        var stateTask = GetEqStateAsync(ip);
        await Task.WhenAll(modesTask, stateTask).ConfigureAwait(false);
        if (modesTask.Result is not { Count: > 0 } modes) return null;
        string current = stateTask.Result?.Source ?? string.Empty;
        if (PickSpareSource(modes, current) is not { } source) return null;
        return (source, modes.First(m => m.Source == source));
    }

    private async Task RestoreSourceAsync(string ip, string source, EqSourceMode mode, Dictionary<string, SourceBands> originals)
    {
        foreach (var (plugin, original) in originals)
        {
            var target = new Dictionary<string, object> { ["source_name"] = source, ["pluginURI"] = plugin };
            var presets = await GetEqPresetListAsync(ip, plugin).ConfigureAwait(false);
            if (original.Name.Length > 0 && (presets.Custom.Contains(original.Name) || presets.BuiltIn.Contains(original.Name)))
                await LoadSourcePresetAsync(ip, source, plugin, original.Name).ConfigureAwait(false);
            else
                await WriteBandsAsync(ip, target, original.ChannelMode, original.Bands).ConfigureAwait(false);
        }
        string activePlugin = mode.PluginUri.Length > 0 ? mode.PluginUri : DefaultEqPlugin;
        await SetEqTypeAsync(ip, source, activePlugin).ConfigureAwait(false);
        if (!mode.Enabled) await TurnEqOffAsync(ip, source, activePlugin).ConfigureAwait(false);
    }

    internal async Task<AmpBackup?> BackupAmpAsync(KnownDevice device)
    {
        string ip = device.Ip;
        if (await GetEqStateAsync(ip).ConfigureAwait(false) == null) return null;
        var graphic = new List<EqPresetBackup>();
        var parametric = new List<EqPresetBackup>();

        var geqNames = (await GetEqPresetListAsync(ip, DefaultEqPlugin).ConfigureAwait(false)).Custom;
        var peqNames = (await GetEqPresetListAsync(ip, PeqPlugin).ConfigureAwait(false)).Custom;
        if ((geqNames.Count > 0 || peqNames.Count > 0) && await FindSpareSourceAsync(ip).ConfigureAwait(false) is { } spare)
        {
            var originals = new Dictionary<string, SourceBands>();
            foreach (var (plugin, names, list) in new[] { (DefaultEqPlugin, geqNames, graphic), (PeqPlugin, peqNames, parametric) })
            {
                if (names.Count == 0) continue;
                var target = new { source_name = spare.Source, pluginURI = plugin };
                if (await ReadBandsAsync(ip, target).ConfigureAwait(false) is { } original) originals[plugin] = original;
                foreach (var name in names)
                {
                    if (!await LoadSourcePresetAsync(ip, spare.Source, plugin, name).ConfigureAwait(false)) continue;
                    if (await ReadBandsAsync(ip, target).ConfigureAwait(false) is { } bands)
                        list.Add(new EqPresetBackup(name, bands.ChannelMode, bands.Bands));
                }
            }
            await RestoreSourceAsync(ip, spare.Source, spare.Mode, originals).ConfigureAwait(false);
        }

        var roomFit = new List<EqPresetBackup>();
        if (await GetRoomFitStateAsync(ip).ConfigureAwait(false) is { } state)
        {
            var target = new { EQLevel = RoomFitLevel, source_name = "", pluginURI = PeqPlugin };
            var original = await ReadBandsAsync(ip, target).ConfigureAwait(false);
            foreach (var name in state.Profiles)
            {
                if (!CommandSucceeded(await SendEqJsonAsync(ip, "EQv2SourceLoad",
                        new { EQLevel = RoomFitLevel, pluginURI = PeqPlugin, Name = name }).ConfigureAwait(false))) continue;
                if (await ReadBandsAsync(ip, target).ConfigureAwait(false) is { } bands)
                    roomFit.Add(new EqPresetBackup(name, bands.ChannelMode, bands.Bands));
            }
            await RestoreRoomFitSelectionAsync(ip, state, original).ConfigureAwait(false);
        }
        return new AmpBackup(device.Name, ip, device.Uuid, graphic, parametric, roomFit);
    }

    private async Task RestoreRoomFitSelectionAsync(string ip, RoomFitState state, SourceBands? original)
    {
        if (state.Active.Length > 0 && (await GetRoomFitStateAsync(ip).ConfigureAwait(false))?.Profiles.Contains(state.Active) == true)
            await SendEqJsonAsync(ip, "EQv2SourceLoad", new { EQLevel = RoomFitLevel, pluginURI = PeqPlugin, Name = state.Active }).ConfigureAwait(false);
        else if (original != null)
            await WriteBandsAsync(ip, new Dictionary<string, object> { ["EQLevel"] = RoomFitLevel, ["source_name"] = "", ["pluginURI"] = PeqPlugin },
                original.ChannelMode, original.Bands).ConfigureAwait(false);
        await SetRoomFitEnabledAsync(ip, state.Enabled).ConfigureAwait(false);
    }

    internal async Task<RestoreResult> RestoreAmpAsync(string ip, AmpBackup backup, bool equalizer, bool roomFit)
    {
        int restored = 0, failed = 0;
        if (equalizer && backup.GraphicEq.Count + backup.ParametricEq.Count > 0)
        {
            if (await FindSpareSourceAsync(ip).ConfigureAwait(false) is { } spare)
            {
                var originals = new Dictionary<string, SourceBands>();
                foreach (var (plugin, presets) in new[] { (DefaultEqPlugin, backup.GraphicEq), (PeqPlugin, backup.ParametricEq) })
                {
                    if (presets.Count == 0) continue;
                    var target = new Dictionary<string, object> { ["source_name"] = spare.Source, ["pluginURI"] = plugin };
                    if (await ReadBandsAsync(ip, target).ConfigureAwait(false) is { } original) originals[plugin] = original;
                    foreach (var preset in presets)
                    {
                        bool ok = CommandSucceeded(await WriteBandsAsync(ip, target, preset.ChannelMode, preset.Bands).ConfigureAwait(false)) &&
                                  await SaveEqPresetAsync(ip, preset.Name, spare.Source, plugin, replacing: true).ConfigureAwait(false);
                        if (ok) restored++;
                        else failed++;
                    }
                }
                await RestoreSourceAsync(ip, spare.Source, spare.Mode, originals).ConfigureAwait(false);
            }
            else failed += backup.GraphicEq.Count + backup.ParametricEq.Count;
        }

        if (roomFit && backup.RoomFit.Count > 0)
        {
            var state = await GetRoomFitStateAsync(ip).ConfigureAwait(false);
            var target = new Dictionary<string, object> { ["EQLevel"] = RoomFitLevel, ["source_name"] = "", ["pluginURI"] = PeqPlugin };
            var original = await ReadBandsAsync(ip, target).ConfigureAwait(false);
            foreach (var profile in backup.RoomFit)
            {
                bool ok = CommandSucceeded(await WriteBandsAsync(ip, target, profile.ChannelMode, profile.Bands).ConfigureAwait(false)) &&
                          CommandSucceeded(await SendEqJsonAsync(ip, "EQSourceSave",
                              new { pluginURI = PeqPlugin, Name = profile.Name, EQLevel = RoomFitLevel }).ConfigureAwait(false));
                if (ok) restored++;
                else failed++;
            }
            if (state != null) await RestoreRoomFitSelectionAsync(ip, state, original).ConfigureAwait(false);
            else await SetRoomFitEnabledAsync(ip, false).ConfigureAwait(false);
        }
        return new RestoreResult(restored, failed);
    }

    internal async Task<EqPresetBackup?> ReadCurrentEqAsync(string ip, string source, string plugin, string name) =>
        await ReadBandsAsync(ip, new { source_name = source, pluginURI = plugin }).ConfigureAwait(false) is { } bands
            ? new EqPresetBackup(name, bands.ChannelMode, bands.Bands)
            : null;

    internal async Task<bool> ImportEqPresetAsync(string ip, string source, string plugin, EqPresetBackup preset)
    {
        var target = new Dictionary<string, object> { ["source_name"] = source, ["pluginURI"] = plugin };
        if (!CommandSucceeded(await WriteBandsAsync(ip, target, preset.ChannelMode, preset.Bands).ConfigureAwait(false))) return false;
        if (!await SaveEqPresetAsync(ip, preset.Name, source, plugin, replacing: true).ConfigureAwait(false)) return false;
        return await LoadSourcePresetAsync(ip, source, plugin, preset.Name).ConfigureAwait(false);
    }

    internal async Task<EqPresetBackup?> ReadActiveRoomFitAsync(string ip)
    {
        if (await GetRoomFitStateAsync(ip).ConfigureAwait(false) is not { Active.Length: > 0 } state) return null;
        return await ReadBandsAsync(ip, new { EQLevel = RoomFitLevel, source_name = "", pluginURI = PeqPlugin }).ConfigureAwait(false) is { } bands
            ? new EqPresetBackup(state.Active, bands.ChannelMode, bands.Bands)
            : null;
    }

    internal async Task<bool> ImportRoomFitAsync(string ip, EqPresetBackup profile)
    {
        bool enabled = (await GetRoomFitStateAsync(ip).ConfigureAwait(false))?.Enabled ?? true;
        var target = new Dictionary<string, object> { ["EQLevel"] = RoomFitLevel, ["source_name"] = "", ["pluginURI"] = PeqPlugin };
        if (!CommandSucceeded(await WriteBandsAsync(ip, target, profile.ChannelMode, profile.Bands).ConfigureAwait(false))) return false;
        if (!CommandSucceeded(await SendEqJsonAsync(ip, "EQSourceSave",
                new { pluginURI = PeqPlugin, Name = profile.Name, EQLevel = RoomFitLevel }).ConfigureAwait(false))) return false;
        return await LoadRoomFitProfileAsync(ip, profile.Name, enabled).ConfigureAwait(false);
    }

    internal BackupFile SinglePresetFile(string ip, EqPresetBackup preset, string kind)
    {
        var device = _knownDevices.FirstOrDefault(d => d.Ip == ip);
        var amp = new AmpBackup(device.Name ?? ip, ip, device.Uuid ?? string.Empty,
            kind == "graphic" ? [preset] : [], kind == "parametric" ? [preset] : [], kind == "roomfit" ? [preset] : []);
        return new BackupFile(BackupApp, AppVersion, DateTime.Now, string.Empty, [amp]);
    }

    internal bool WriteConfigText(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cfgPath)!);
            File.WriteAllText(_cfgPath, text);
            _configLocked = true;
            return true;
        }
        catch (Exception ex)
        {
            Notify($"Couldn't write the settings: {ex.Message}");
            return false;
        }
    }

    internal bool CanRestart => OperatingSystem.IsWindows() && Environment.ProcessPath != null;

    internal void Restart()
    {
        if (CanRestart)
        {
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restart") { UseShellExecute = false }); }
            catch (Exception) { }
        }
        Exit();
    }
}
