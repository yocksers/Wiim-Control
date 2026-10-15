using System.Text.Json;
using System.Text.Json.Serialization;

namespace WiimControl;

sealed record RecentLibrary(string ServerName, string ServerUdn, string ServerUrl, bool IsLyrion, string FolderId, int FolderIndex);

sealed record RecentItem(string Title, string Detail, string ArtUrl, DateTime Played, RadioStation? Station, RecentLibrary? Library)
{
    [JsonIgnore] public string Key => Station != null ? "radio:" + Station.Url : $"library:{Library?.ServerUdn}:{Library?.FolderId}";
}

sealed partial class WiimController
{
    private const int MaxRecent = 30;
    private static readonly JsonSerializerOptions RecentJson = new();
    private List<RecentItem> _recent = [];

    internal IReadOnlyList<RecentItem> Recent => _recent;

    internal event Action? RecentChanged;

    internal void AddRecent(RecentItem item)
    {
        _recent.RemoveAll(r => r.Key == item.Key);
        _recent.Insert(0, item);
        if (_recent.Count > MaxRecent) _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
        SaveConfig();
        RecentChanged?.Invoke();
    }

    internal void ClearRecent()
    {
        _recent.Clear();
        SaveConfig();
        RecentChanged?.Invoke();
    }

    private string SerializeRecent() => JsonSerializer.Serialize(_recent, RecentJson);

    private static List<RecentItem> ParseRecent(string value)
    {
        try { return JsonSerializer.Deserialize<List<RecentItem>>(value, RecentJson)?.Where(r => r.Station != null || r.Library != null).ToList() ?? []; }
        catch (JsonException) { return []; }
    }
}
