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

    internal async Task PlayPresetAsync(int preset)
    {
        string target = PlaybackTarget;
        if (BlockedByLock(_deviceIp, target)) return;
        await SendPlayerCommandAsync(target, $"MCUKeyShortClick:{preset}");
    }

    internal async Task SendMediaCommandAsync(string action)
    {
        string wiimCmd = action switch
        {
            "playpause" => "onepause",
            _           => action
        };
        string target = PlaybackTarget;
        if (BlockedByLock(_deviceIp, target)) return;
        if (await TryExternalCommandAsync(target, wiimCmd).ConfigureAwait(false)) return;
        await SendPlayerCommandAsync(target, $"setPlayerCmd:{wiimCmd}").ConfigureAwait(false);
    }

    internal async Task<string> TestConnectionAsync()
    {
        string ip = _deviceIp;
        try
        {
            string statusEx = await _http.GetStringAsync($"https://{ip}/httpapi.asp?command=getStatusEx");
            string name = JsonField(statusEx, "DeviceName") is { Length: > 0 } deviceName ? deviceName : AmpName(ip);
            string model = JsonField(statusEx, "project") is { Length: > 0 } project ? $" ({ModelName(project)})" : string.Empty;
            return $"Connected to {name}{model} at {ip}.";
        }
        catch (Exception ex)
        {
            return ConnectionProblem(ip, ex, OperatingSystem.IsMacOS());
        }
    }

    internal static string ConnectionProblem(string ip, Exception ex, bool mac)
    {
        var socket = ex as System.Net.Sockets.SocketException ?? ex.InnerException as System.Net.Sockets.SocketException;
        const string MacHint = " On a Mac, also check System Settings → Privacy & Security → Local Network and make sure Wiim Control is allowed.";
        return socket?.SocketErrorCode switch
        {
            System.Net.Sockets.SocketError.HostUnreachable or System.Net.Sockets.SocketError.NetworkUnreachable =>
                $"This computer can't reach {ip}. Check that the device is switched on and on the same network." + (mac ? MacHint : string.Empty),
            System.Net.Sockets.SocketError.ConnectionRefused => $"{ip} refused the connection. Check that the address belongs to a WiiM device.",
            System.Net.Sockets.SocketError.HostNotFound or System.Net.Sockets.SocketError.NoData => $"{ip} wasn't found on the network. Check the address.",
            System.Net.Sockets.SocketError.TimedOut => $"{ip} didn't answer in time. Check that the device is switched on and on the same network.",
            _ when ex is TaskCanceledException or TimeoutException =>
                $"{ip} didn't answer in time. Check that the device is switched on and on the same network." + (mac ? MacHint : string.Empty),
            _ when ex is HttpRequestException { StatusCode: { } code } => $"{ip} answered, but with an error (HTTP {(int)code}). It may not be a WiiM device.",
            _ => $"Couldn't connect to {ip}: {ex.Message}"
        };
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
        if (string.IsNullOrEmpty(ip) || BlockedByLock(ip)) return;
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
        if (leader != _deviceIp && !IsDeviceLocked(leader) && await GetDeviceVolumeAsync(leader).ConfigureAwait(false) is { } leaderVolume)
            await SendToDeviceAsync(leader, $"setPlayerCmd:vol:{Math.Clamp(leaderVolume.Volume + delta, 0, 100)}").ConfigureAwait(false);
        var json = await SendToDeviceAsync(leader, "multiroom:getSlaveList").ConfigureAwait(false);
        foreach (var (ip, volume) in ParseSlaveVolumes(json))
        {
            if (ip == _deviceIp || ip == ownAddress || IsDeviceLocked(ip)) continue;
            await SendToDeviceAsync(leader, $"multiroom:SlaveVolume:{ip}:{Math.Clamp(volume + delta, 0, 100)}").ConfigureAwait(false);
        }
    }

    private async Task MuteGroupAsync(bool muted)
    {
        string leader = PlaybackTarget;
        if (leader != _deviceIp && !IsDeviceLocked(leader))
            await SendToDeviceAsync(leader, $"setPlayerCmd:mute:{(muted ? 1 : 0)}").ConfigureAwait(false);
        await SendToDeviceAsync(leader, $"setPlayerCmd:slave_mute:{(muted ? "mute" : "unmute")}").ConfigureAwait(false);
    }

    private async Task ToggleMuteAsync()
    {
        string ip = _deviceIp;
        if (string.IsNullOrEmpty(ip) || BlockedByLock(ip)) return;
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
        if (match.Ip is null) return;

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (match.Ip == _deviceIp) return;
            string oldIp = _deviceIp;
            _consecutiveFailures = 0;
            if (_knownDevices.Any(k => k.Ip == oldIp)) RelocateDevice(oldIp, match.Ip);
            else _deviceIp = match.Ip;
            for (int i = 0; i < _knownDevices.Count; i++)
                if (SameDeviceId(_knownDevices[i].Uuid, _deviceUuid) && _knownDevices[i].Ip != match.Ip)
                    _knownDevices[i] = _knownDevices[i] with { Ip = match.Ip };
            SaveConfig();
            RefreshDeviceStatusUi();
            Notify($"WiiM device reconnected at {match.Ip}");
        });
    }
}
