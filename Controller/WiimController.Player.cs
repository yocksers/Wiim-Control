namespace WiimControl;

sealed partial class WiimController
{
    internal async Task PlayPresetAsync(int preset)
    {
        try
        {
            var url = $"https://{PlaybackTarget}/httpapi.asp?command=MCUKeyShortClick:{preset}";
            await _http.GetAsync(url).ConfigureAwait(false);
            OnWiimRequestSucceeded();
        }
        catch (Exception ex)
        {
            Notify($"Error: {ex.Message}");
            _ = OnWiimRequestFailedAsync();
        }
    }

    internal async Task SendMediaCommandAsync(string action)
    {
        string wiimCmd = action switch
        {
            "playpause" => "onepause",
            _           => action
        };
        try
        {
            var url = $"https://{PlaybackTarget}/httpapi.asp?command=setPlayerCmd:{wiimCmd}";
            await _http.GetAsync(url).ConfigureAwait(false);
            OnWiimRequestSucceeded();
        }
        catch (Exception ex)
        {
            Notify($"Error: {ex.Message}");
            _ = OnWiimRequestFailedAsync();
        }
    }

    internal async Task<int> TestConnectionAsync()
    {
        var resp = await _http.GetAsync($"https://{_deviceIp}/httpapi.asp?command=getPlayerStatus");
        return (int)resp.StatusCode;
    }

    private async Task<(int Volume, bool Muted)?> GetPlayerStatusAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(
                $"https://{_deviceIp}/httpapi.asp?command=getPlayerStatus").ConfigureAwait(false);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("vol", out var volProp)) return null;
            if (!int.TryParse(volProp.GetString(), out int vol)) return null;
            bool muted = doc.RootElement.TryGetProperty("mute", out var muteProp) && muteProp.GetString() == "1";
            OnWiimRequestSucceeded();
            return (vol, muted);
        }
        catch (Exception ex)
        {
            Notify($"Error: {ex.Message}");
            _ = OnWiimRequestFailedAsync();
            return null;
        }
    }

    private int ApplyVolumeStep(int current, int direction)
    {
        if (!_logStep) return Math.Clamp(current + direction * _volumeStep, 0, 100);

        double db = 20.0 * Math.Log10(Math.Max(current, 1) / 100.0) + direction * 1.5;
        int next = (int)Math.Round(100.0 * Math.Pow(10.0, db / 20.0));
        return Math.Clamp(next, 0, 100);
    }

    private async Task ChangeVolumeAsync(int direction)
    {
        int current;
        bool muted;
        if (_cachedVolume is int cv && _cachedMuted is bool cm && DateTime.UtcNow - _cacheSyncUtc < CacheLifetime)
        {
            current = cv;
            muted = cm;
        }
        else
        {
            var status = await GetPlayerStatusAsync().ConfigureAwait(false);
            if (status is not { } s) return;
            current = s.Volume;
            muted = s.Muted;
        }

        int newVol = Math.Min(ApplyVolumeStep(current, direction), _activeMaxVolume);
        try
        {
            await _http.GetAsync(
                $"https://{_deviceIp}/httpapi.asp?command=setPlayerCmd:vol:{newVol}").ConfigureAwait(false);
            OnWiimRequestSucceeded();
        }
        catch (Exception ex)
        {
            Notify($"Error: {ex.Message}");
            _ = OnWiimRequestFailedAsync();
            return;
        }

        _cachedVolume = newVol;
        _cachedMuted  = muted;
        _cacheSyncUtc = DateTime.UtcNow;
        ShowVolumeOsd(newVol, muted);
        if (_groupVolumeKeys) _ = AdjustGroupVolumeAsync(newVol - current);
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
        int volume;
        bool isMuted;
        if (_cachedVolume is int cv && _cachedMuted is bool cm && DateTime.UtcNow - _cacheSyncUtc < CacheLifetime)
        {
            volume = cv;
            isMuted = cm;
        }
        else
        {
            var status = await GetPlayerStatusAsync().ConfigureAwait(false);
            if (status is not { } s) return;
            volume = s.Volume;
            isMuted = s.Muted;
        }

        try
        {
            await _http.GetAsync(
                $"https://{_deviceIp}/httpapi.asp?command=setPlayerCmd:mute:{(isMuted ? 0 : 1)}").ConfigureAwait(false);
            OnWiimRequestSucceeded();
        }
        catch (Exception ex)
        {
            Notify($"Error: {ex.Message}");
            _ = OnWiimRequestFailedAsync();
            return;
        }

        _cachedVolume = volume;
        _cachedMuted  = !isMuted;
        _cacheSyncUtc = DateTime.UtcNow;
        ShowVolumeOsd(volume, !isMuted);
        if (_groupVolumeKeys) _ = MuteGroupAsync(!isMuted);
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

        var match = found.FirstOrDefault(d => d.Uuid == _deviceUuid);
        if (match.Ip is null || match.Ip == _deviceIp) return;

        _deviceIp = match.Ip;
        _consecutiveFailures = 0;
        for (int i = 0; i < _knownDevices.Count; i++)
            if (_knownDevices[i].Uuid == _deviceUuid)
                _knownDevices[i] = match;
        SaveConfig();

        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshDeviceStatusUi);
        Notify($"Wiim Amp reconnected at {match.Ip}");
    }
}
