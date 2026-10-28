using Avalonia;
using Avalonia.Controls;

namespace WiimControl;

sealed partial class WiimController
{
    private static readonly TimeSpan NowPlayingInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FastNowPlayingInterval = TimeSpan.FromSeconds(4);

    private readonly record struct MiniPlayerPlacement(PixelPoint Position, Size Size, bool Pinned = false);

    private readonly Dictionary<string, MiniPlayerWindow> _miniPlayers = new();
    private readonly Dictionary<string, MiniPlayerPlacement> _miniPlayerPlacements = new();
    private readonly Dictionary<string, MiniPlayerPlacement> _miniPlayerCompactPlacements = new();
    private readonly HashSet<string> _miniPlayerCompactIps = new();
    private bool _miniPlayerAuto;
    private bool _miniPlayerOnTop;
    private bool _miniPlayerCompactOnTop;
    private readonly Dictionary<string, string?> _lastInputs = new();

    internal bool MiniPlayerAuto
    {
        get => _miniPlayerAuto;
        set
        {
            _miniPlayerAuto = value;
            UpdateNowPlayingInterval();
            SaveConfig();
        }
    }

    internal bool MiniPlayerOnTop
    {
        get => _miniPlayerOnTop;
        set
        {
            _miniPlayerOnTop = value;
            foreach (var window in _miniPlayers.Values) window.ApplyTopmost();
            SaveConfig();
        }
    }

    internal bool MiniPlayerCompactOnTop
    {
        get => _miniPlayerCompactOnTop;
        set
        {
            _miniPlayerCompactOnTop = value;
            foreach (var window in _miniPlayers.Values) window.ApplyTopmost();
            SaveConfig();
        }
    }

    internal void ShowMiniPlayer(string ip, bool activate = true)
    {
        if (_miniPlayers.TryGetValue(ip, out var open))
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            if (activate) open.Activate();
            return;
        }
        var window = new MiniPlayerWindow(this, ip, _miniPlayers.Count) { ShowActivated = activate };
        window.Closed += (_, _) => { if (_miniPlayers.TryGetValue(ip, out var current) && ReferenceEquals(current, window)) _miniPlayers.Remove(ip); };
        _miniPlayers[ip] = window;
        window.Show();
    }

    internal bool IsMiniPlayerOpen(string ip) => _miniPlayers.ContainsKey(ip);

    internal void ToggleMiniPlayer(string ip)
    {
        if (IsMiniPlayerOpen(ip)) CloseMiniPlayer(ip);
        else ShowMiniPlayer(ip);
    }

    internal void CloseMiniPlayer(string ip)
    {
        if (_miniPlayers.TryGetValue(ip, out var window)) window.Close();
    }

    private void CloseMiniPlayers()
    {
        foreach (var window in _miniPlayers.Values.ToList()) window.Close();
    }

    internal void ResetMiniPlayerPlacement()
    {
        _miniPlayerPlacements.Clear();
        _miniPlayerCompactPlacements.Clear();
        int slot = 0;
        foreach (var window in _miniPlayers.Values.ToList()) window.PlaceAtDefault(slot++);
        SaveConfig();
    }

    private void MoveMiniPlayerPlacement(string oldIp, string newIp)
    {
        if (_miniPlayerPlacements.Remove(oldIp, out var placement)) _miniPlayerPlacements[newIp] = placement;
        if (_miniPlayerCompactPlacements.Remove(oldIp, out var compact)) _miniPlayerCompactPlacements[newIp] = compact;
        if (_miniPlayerCompactIps.Remove(oldIp)) _miniPlayerCompactIps.Add(newIp);
        CloseMiniPlayer(oldIp);
    }

    private bool IsMiniPlayerShowingArtwork(Avalonia.Media.Imaging.Bitmap bitmap) =>
        _miniPlayers.Values.Any(w => w.IsShowingArtwork(bitmap));

    private static void ParseMiniPlayerPlacements(string value, Dictionary<string, MiniPlayerPlacement> placements)
    {
        foreach (var entry in value.Split('|', StringSplitOptions.RemoveEmptyEntries))
            if (entry.Split(',') is [var ip, var x, var y, var width, var height, .. var rest] && rest.Length <= 1 && ip.Length > 0 &&
                int.TryParse(x, out int left) && int.TryParse(y, out int top) &&
                int.TryParse(width, out int w) && int.TryParse(height, out int h) && w > 0 && h > 0)
                placements[ip] = new MiniPlayerPlacement(new PixelPoint(left, top), new Size(w, h), rest is ["1"]);
    }

    private static string SerializeMiniPlayerPlacements(Dictionary<string, MiniPlayerPlacement> placements) =>
        string.Join('|', placements.Select(p =>
            $"{p.Key},{p.Value.Position.X},{p.Value.Position.Y},{(int)p.Value.Size.Width},{(int)p.Value.Size.Height}{(p.Value.Pinned ? ",1" : "")}"));

    internal void NoteDeviceInput(string ip, DeviceStatus? status)
    {
        if (status == null) return;
        string? input = InputOfMode(status.Mode);
        bool known = _lastInputs.TryGetValue(ip, out var previous);
        _lastInputs[ip] = input;
        if (!known || !_miniPlayerAuto || ip != _deviceIp || input != "wifi" || previous is null or "wifi") return;
        if (_miniPlayers.ContainsKey(ip)) return;
        ShowMiniPlayer(ip, activate: false);
    }
}
