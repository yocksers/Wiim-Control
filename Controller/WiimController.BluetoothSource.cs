using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    private const int BluetoothMode = 41;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private enum BluetoothSource { ThisComputer, OtherDevice }

    private readonly ConcurrentDictionary<string, BluetoothSource> _bluetoothSources = new();

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    internal bool IsBluetoothInput(string? ip) => !string.IsNullOrEmpty(ip) && _bluetoothSources.ContainsKey(ip);

    internal bool IsBluetoothFromThisComputer(string? ip) =>
        !string.IsNullOrEmpty(ip) && _bluetoothSources.TryGetValue(ip, out var source) && source == BluetoothSource.ThisComputer;

    private void NoteBluetoothSource(string ip, int mode, string sourceName)
    {
        if (mode != BluetoothMode) _bluetoothSources.TryRemove(ip, out _);
        else _bluetoothSources[ip] = ThisComputer.Names.Contains(sourceName, StringComparer.OrdinalIgnoreCase)
            ? BluetoothSource.ThisComputer
            : BluetoothSource.OtherDevice;
        if (ip == _deviceIp) Dispatcher.UIThread.Post(UpdateVolumeKeyRegistration);
    }

    private void HandleMediaKey(string cmd)
    {
        if (cmd is "vol++" or "vol--" or "mute" && IsBluetoothInput(_deviceIp) && !IsBluetoothFromThisComputer(_deviceIp))
        {
            _ = ChangeComputerVolumeAsync(cmd);
            return;
        }
        HandleCommand(cmd);
    }

    private static void PressVolumeKey(string cmd)
    {
        byte key = (byte)(cmd switch { "vol++" => VK_VOLUME_UP, "vol--" => VK_VOLUME_DOWN, _ => VK_VOLUME_MUTE });
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    internal async Task<DeviceVolume?> GetVolumeAsync(string ip) =>
        IsBluetoothFromThisComputer(ip)
            ? await ThisComputer.GetVolumeAsync().ConfigureAwait(false)
            : await GetDeviceVolumeAsync(ip).ConfigureAwait(false);

    internal async Task<bool> SetVolumeAsync(string ip, int volume) =>
        IsBluetoothFromThisComputer(ip)
            ? await ThisComputer.SetVolumeAsync(volume).ConfigureAwait(false)
            : await SetDeviceVolumeAsync(ip, volume).ConfigureAwait(false);

    internal async Task<bool> SetMuteAsync(string ip, bool muted) =>
        IsBluetoothFromThisComputer(ip)
            ? await ThisComputer.SetMutedAsync(muted).ConfigureAwait(false)
            : await SetDeviceMuteAsync(ip, muted).ConfigureAwait(false);

    private async Task ChangeComputerVolumeAsync(string cmd)
    {
        if (OperatingSystem.IsWindows())
        {
            PressVolumeKey(cmd);
            return;
        }
        string ip = _deviceIp;
        if (await ThisComputer.GetVolumeAsync().ConfigureAwait(false) is not { } current) return;
        var next = cmd == "mute"
            ? current with { Muted = !current.Muted }
            : current with { Volume = ApplyVolumeStep(current.Volume, cmd == "vol++" ? 1 : -1) };
        bool ok = cmd == "mute"
            ? await ThisComputer.SetMutedAsync(next.Muted).ConfigureAwait(false)
            : await ThisComputer.SetVolumeAsync(next.Volume).ConfigureAwait(false);
        if (!ok) return;
        ShowVolumeOsd(next.Volume, next.Muted);
        Dispatcher.UIThread.Post(() => _settingsWindow?.ShowVolume(ip, next.Volume, next.Muted));
    }
}
