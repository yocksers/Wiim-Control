using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Threading;

namespace WiimControl;

sealed partial class WiimController
{
    internal const string PcAudioTitle = "Sound from this computer";
    private static readonly TimeSpan PcAudioIdleLimit = TimeSpan.FromSeconds(20);

    private IPcAudioCapture? _pcCapture;
    private PcAudioServer? _pcServer;
    private string? _pcAudioIp;
    private NativeMenuItem? _mnuPcAudio;
    private readonly DispatcherTimer _pcAudioWatch = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _pcAudioWatchHooked;

    internal static readonly bool PcAudioSupported = OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && PulseMonitorCapture.Available);

    internal bool IsPlayingPcAudio(string ip) => _pcAudioIp == ip;

    internal static string? LocalAddressFor(string deviceIp)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(IPAddress.Parse(deviceIp), 9);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString();
        }
        catch (Exception ex) when (ex is SocketException or FormatException) { return null; }
    }

    internal async Task<bool> SetPcAudioAsync(string ip, bool on)
    {
        bool ok = on ? await StartPcAudioAsync(ip) : await StopPcAudioAsync();
        SyncPcAudioUi();
        return ok;
    }

    private async Task<bool> StartPcAudioAsync(string ip)
    {
        if (!PcAudioSupported) return false;
        StopPcAudio();
        if (LocalAddressFor(ip) is not { } local) return false;
        if (!StartPcCapture()) return false;
        _pcServer!.AllowedClient = IPAddress.TryParse(ip, out var device) ? device : null;
        string url = $"http://{local}:{_pcServer.Port}{PcAudioServer.StreamPath}";
        if (!CommandSucceeded(await SendToDeviceAsync(ip, $"setPlayerCmd:play:{url}")))
        {
            StopPcAudio();
            return false;
        }
        _pcAudioIp = ip;
        if (!_pcAudioWatchHooked)
        {
            _pcAudioWatchHooked = true;
            _pcAudioWatch.Tick += (_, _) => CheckPcAudioListener();
        }
        _pcAudioWatch.Start();
        return true;
    }

    private static IPcAudioCapture CreatePcCapture() =>
        OperatingSystem.IsWindows() ? new LoopbackCapture() : new PulseMonitorCapture();

    private bool StartPcCapture(PcAudioServer? keep = null)
    {
        if (!PcAudioSupported) return false;
        try
        {
            var capture = CreatePcCapture();
            var server = keep != null && keep.SampleRate == capture.SampleRate ? keep : new PcAudioServer(capture.SampleRate);
            capture.Pcm += server.Write;
            capture.Stopped += () => Dispatcher.UIThread.Post(() => OnPcCaptureStopped(capture));
            _pcCapture?.Dispose();
            if (_pcServer != null && !ReferenceEquals(_pcServer, server)) _pcServer.Dispose();
            _pcCapture = capture;
            _pcServer = server;
            if (_pcAudioIp != null && IPAddress.TryParse(_pcAudioIp, out var target)) server.AllowedClient = target;
            return true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or SocketException or
                                       InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ErrorLog.Write("PC sound", ex);
            StopPcAudio();
            return false;
        }
    }

    private async Task<bool> StopPcAudioAsync()
    {
        string? ip = _pcAudioIp;
        StopPcAudio();
        return ip == null || CommandSucceeded(await SendToDeviceAsync(ip, "setPlayerCmd:stop"));
    }

    internal void StopPcAudio()
    {
        _pcAudioWatch.Stop();
        _pcCapture?.Dispose();
        _pcServer?.Dispose();
        _pcCapture = null;
        _pcServer = null;
        _pcAudioIp = null;
    }

    internal void OnInputChosen(string ip)
    {
        if (_pcAudioIp != ip) return;
        StopPcAudio();
        SyncPcAudioUi();
    }

    private void CheckPcAudioListener()
    {
        if (_pcServer is not { } server || server.ClientCount > 0 || DateTime.UtcNow - server.LastClientSeen < PcAudioIdleLimit) return;
        StopPcAudio();
        SyncPcAudioUi();
    }

    private static readonly TimeSpan PcCaptureRestartDelay = TimeSpan.FromSeconds(1.5);

    private void OnPcCaptureStopped(IPcAudioCapture capture)
    {
        if (!ReferenceEquals(capture, _pcCapture) || _pcAudioIp is not { } ip) return;
        DispatcherTimer.RunOnce(async () =>
        {
            if (!ReferenceEquals(capture, _pcCapture) || _pcAudioIp != ip) return;
            OnDefaultOutputChanged();
            if (_pcAudioIp == ip && !ReferenceEquals(capture, _pcCapture)) return;
            StopPcAudio();
            SyncPcAudioUi();
            Notify("Casting this computer's sound stopped because its sound device is no longer available.");
            await SendToDeviceAsync(ip, "setPlayerCmd:stop");
        }, PcCaptureRestartDelay);
    }

    private void OnDefaultOutputChanged()
    {
        if (_pcAudioIp is not { } ip || _pcServer is not { } server) return;
        int rate = server.SampleRate;
        if (!StartPcCapture(server))
        {
            SyncPcAudioUi();
            return;
        }
        if (_pcServer?.SampleRate != rate || !ReferenceEquals(_pcServer, server)) _ = SetPcAudioAsync(ip, true);
    }

    private void SyncPcAudioUi()
    {
        if (_mnuPcAudio != null) _mnuPcAudio.IsChecked = _pcAudioIp != null && _pcAudioIp == _deviceIp;
        _settingsWindow?.OnPcAudioChanged();
    }

    private NativeMenuItem BuildPcAudioMenuItem()
    {
        _mnuPcAudio = new NativeMenuItem("Stream audio from this computer over network to the device") { ToggleType = NativeMenuItemToggleType.CheckBox };
        _mnuPcAudio.Click += async (_, _) =>
        {
            if (!await SetPcAudioAsync(_deviceIp, !IsPlayingPcAudio(_deviceIp)))
                Notify("The device didn't start playing this computer's sound.");
        };
        return _mnuPcAudio;
    }

    private DeviceStatus WithPcAudioTitle(string ip, DeviceStatus status) =>
        _pcAudioIp == ip && IsNetworkMode(status.Mode) &&
        (status.Title.Length == 0 || status.Title.Contains(PcAudioServer.StreamPath.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
            ? status with { Title = PcAudioTitle, Artist = Environment.MachineName, Album = string.Empty, BitDepth = PcAudioServer.StreamBits }
            : status;
}
