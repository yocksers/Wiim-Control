namespace WiimControl;

sealed partial class WiimController
{
    private async Task<bool> SendPlayerCommandAsync(string ip, string command)
    {
        if (string.IsNullOrEmpty(ip)) return false;
        try
        {
            using var response = await _http.GetAsync($"https://{ip}/httpapi.asp?command={Uri.EscapeDataString(command)}").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"The device answered with HTTP {(int)response.StatusCode}.", null, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (ip == _deviceIp) OnWiimRequestSucceeded();
            if (CommandSucceeded(body)) return true;
            Notify("The device didn't accept the command.");
            return false;
        }
        catch (Exception ex)
        {
            NotifyRequestFailed(ip, ex);
            if (ip == _deviceIp) _ = OnWiimRequestFailedAsync();
            return false;
        }
    }

    internal Task PlayPresetAsync(int preset) => SendPlayerCommandAsync(PlaybackTarget, $"MCUKeyShortClick:{preset}");

    internal async Task SendMediaCommandAsync(string action)
    {
        string wiimCmd = action switch
        {
            "playpause" => "onepause",
            _           => action
        };
        string target = PlaybackTarget;
        if (await TryExternalCommandAsync(target, wiimCmd).ConfigureAwait(false)) return;
        await SendPlayerCommandAsync(target, $"setPlayerCmd:{wiimCmd}").ConfigureAwait(false);
    }

    internal async Task<int> TestConnectionAsync()
    {
        using var resp = await _http.GetAsync($"https://{_deviceIp}/httpapi.asp?command=getPlayerStatus");
        return (int)resp.StatusCode;
    }

    private async Task<(int Volume, bool Muted)?> GetPlayerStatusAsync(string ip)
    {
        try
        {
            var json = await GetAmpStringAsync($"https://{ip}/httpapi.asp?command=getPlayerStatus").ConfigureAwait(false);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("vol", out var volProp)) return null;
            if (!int.TryParse(volProp.GetString(), out int vol)) return null;
            bool muted = doc.RootElement.TryGetProperty("mute", out var muteProp) && muteProp.GetString() == "1";
            if (ip == _deviceIp) OnWiimRequestSucceeded();
            return (vol, muted);
        }
        catch (Exception ex)
        {
            NotifyRequestFailed(ip, ex);
            if (ip == _deviceIp) _ = OnWiimRequestFailedAsync();
            return null;
        }
    }

    private async Task<(int Volume, bool Muted)?> GetVolumeStateAsync(string ip)
    {
        if (_cachedIp == ip && _cachedVolume is int volume && _cachedMuted is bool muted && DateTime.UtcNow - _cacheSyncUtc < CacheLifetime)
            return (volume, muted);
        return await GetPlayerStatusAsync(ip).ConfigureAwait(false);
    }

    private void StoreVolumeState(string ip, int volume, bool muted)
    {
        if (ip != _deviceIp) return;
        _cachedIp = ip;
        _cachedVolume = volume;
        _cachedMuted = muted;
        _cacheSyncUtc = DateTime.UtcNow;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _settingsWindow?.ShowVolume(ip, volume, muted));
    }

    private int ApplyVolumeStep(int current, int direction)
    {
        if (!_logStep) return Math.Clamp(current + direction * _volumeStep, 0, 100);
        if (direction < 0 && current <= 0) return 0;

        double db = 20.0 * Math.Log10(Math.Max(current, 1) / 100.0) + direction * 1.5;
        int next = (int)Math.Round(100.0 * Math.Pow(10.0, db / 20.0));
        next = direction > 0 ? Math.Max(next, current + 1) : Math.Min(next, current - 1);
        return Math.Clamp(next, 0, 100);
    }

    private async Task ChangeVolumeAsync(int direction)
    {
        string ip = _deviceIp;
        if (string.IsNullOrEmpty(ip)) return;
        if (!await _volumeGate.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false)) return;
        try
        {
            if (await GetVolumeStateAsync(ip).ConfigureAwait(false) is not { } state) return;
            int newVol = ApplyVolumeStep(state.Volume, direction);
            if (!await SendPlayerCommandAsync(ip, $"setPlayerCmd:vol:{newVol}").ConfigureAwait(false)) return;
            if (ip != _deviceIp) return;
            StoreVolumeState(ip, newVol, state.Muted);
            ShowVolumeOsd(newVol, state.Muted);
            if (_groupVolumeKeys) _ = AdjustGroupVolumeAsync(newVol - state.Volume);
        }
        finally { _volumeGate.Release(); }
    }

    private async Task AdjustGroupVolumeAsync(int delta)
    {
        if (delta == 0) return;
        string leader = PlaybackTarget;
        string ownAddress = _multiroomCache?.AddressOf(_deviceIp) ?? _deviceIp;
        if (leader != _deviceIp && await GetDeviceVolumeAsync(leader).ConfigureAwait(false) is { } leaderVolume)
            await SendToDeviceAsync(leader, $"setPlayerCmd:vol:{Math.Clamp(leaderVolume.Volume + delta, 0, 100)}").ConfigureAwait(false);
        var json = await SendToDeviceAsync(leader, "multiroom:getSlaveList").ConfigureAwait(false);
        foreach (var (ip, volume) in ParseSlaveVolumes(json))
        {
            if (ip == _deviceIp || ip == ownAddress) continue;
            await SendToDeviceAsync(leader, $"multiroom:SlaveVolume:{ip}:{Math.Clamp(volume + delta, 0, 100)}").ConfigureAwait(false);
        }
    }

    private async Task MuteGroupAsync(bool muted)
    {
        string leader = PlaybackTarget;
        if (leader != _deviceIp)
            await SendToDeviceAsync(leader, $"setPlayerCmd:mute:{(muted ? 1 : 0)}").ConfigureAwait(false);
        await SendToDeviceAsync(leader, $"setPlayerCmd:slave_mute:{(muted ? "mute" : "unmute")}").ConfigureAwait(false);
    }

    private async Task ToggleMuteAsync()
    {
        string ip = _deviceIp;
        if (string.IsNullOrEmpty(ip)) return;
        if (!await _volumeGate.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false)) return;
        try
        {
            if (await GetVolumeStateAsync(ip).ConfigureAwait(false) is not { } state) return;
            bool muted = !state.Muted;
            if (!await SendPlayerCommandAsync(ip, $"setPlayerCmd:mute:{(muted ? 1 : 0)}").ConfigureAwait(false)) return;
            if (ip != _deviceIp) return;
            StoreVolumeState(ip, state.Volume, muted);
            ShowVolumeOsd(state.Volume, muted);
            if (_groupVolumeKeys) _ = MuteGroupAsync(muted);
        }
        finally { _volumeGate.Release(); }
    }

    private void OnWiimRequestSucceeded() => _consecutiveFailures = 0;

    private async Task OnWiimRequestFailedAsync()
    {
        _consecutiveFailures++;
        if (_consecutiveFailures < 3) return;
        if (DateTime.UtcNow - _lastReconnectAttempt < TimeSpan.FromSeconds(30)) return;
        _lastReconnectAttempt = DateTime.UtcNow;
        await TryReconnectAsync();
    }

    private async Task TryReconnectAsync()
    {
        if (string.IsNullOrEmpty(_deviceUuid)) return;

        List<KnownDevice> found;
        try { found = await WiimDiscovery.DiscoverAsync(TimeSpan.FromSeconds(3)); }
        catch { return; }

        var match = found.FirstOrDefault(d => SameDeviceId(d.Uuid, _deviceUuid));
        if (match.Ip is null || match.Ip == _deviceIp) return;

        string oldIp = _deviceIp;
        _deviceIp = match.Ip;
        _consecutiveFailures = 0;
        for (int i = 0; i < _knownDevices.Count; i++)
            if (_knownDevices[i].Ip == oldIp || SameDeviceId(_knownDevices[i].Uuid, _deviceUuid))
                _knownDevices[i] = _knownDevices[i] with { Ip = match.Ip };
        SaveConfig();

        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshDeviceStatusUi);
        Notify($"WiiM device reconnected at {match.Ip}");
    }
}
