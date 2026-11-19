using System.Security;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WiimControl;

sealed record PlayingItem(string Url, string Metadata, string Title, string ArtUrl, string Source);

sealed partial class WiimController
{
    private const string EmptyKey = "<RoutineId>Empty</RoutineId>";

    private static Regex KeyPattern(int number) => new($@"<Key{number}>(.*?)</Key{number}>|<Key{number}\s*/>", RegexOptions.Singleline);

    internal static string? KeyContent(string keyList, int number) =>
        KeyPattern(number).Match(keyList) is { Success: true } m ? m.Groups[1].Value : null;

    internal static string? ReplaceKey(string keyList, int number, string content)
    {
        var pattern = KeyPattern(number);
        return pattern.IsMatch(keyList) ? pattern.Replace(keyList, $"<Key{number}>{content}</Key{number}>", 1) : null;
    }

    private static string Tag(string name, string value) => $"<{name}>{SecurityElement.Escape(value)}</{name}>";

    internal static string MusicKey(PlayingItem item, string name) =>
        Tag("Name", name) + Tag("PicUrl", item.ArtUrl) + Tag("Source", item.Source) + EmptyKey +
        Tag("Shuffle", "0") + Tag("Loop", "0") + Tag("Url", item.Url) + Tag("Metadata", item.Metadata);

    internal static string RenameKey(string content, string name)
    {
        var existing = new Regex("<Name>.*?</Name>", RegexOptions.Singleline);
        return existing.IsMatch(content) ? existing.Replace(content, Tag("Name", name), 1) : Tag("Name", name) + content;
    }

    internal static bool IsMusicKey(string? content) => content != null && Regex.IsMatch(content, @"<Url>\s*\S", RegexOptions.Singleline);

    private async Task<string?> GetKeyListAsync(string ip)
    {
        var response = await QueueCallAsync(ip, "GetKeyMapping").ConfigureAwait(false);
        string list = Child(response, "QueueContext");
        return list.Contains("<KeyList", StringComparison.Ordinal) ? list : null;
    }

    private async Task<bool> UpdateKeyAsync(string ip, int number, Func<string, string?> change, Func<string?, bool> verify)
    {
        if (await GetKeyListAsync(ip).ConfigureAwait(false) is not { } list || KeyContent(list, number) is not { } content) return false;
        if (change(content) is not { } updated || ReplaceKey(list, number, updated) is not { } newList) return false;
        if (await QueueCallAsync(ip, "SetKeyMapping", ("QueueContext", newList)).ConfigureAwait(false) == null) return false;
        var check = await GetKeyListAsync(ip).ConfigureAwait(false);
        return check != null && verify(KeyContent(check, number));
    }

    private static string? KeyValue(string? content, string name) =>
        content != null && Regex.Match(content, $"<{name}>(.*?)</{name}>", RegexOptions.Singleline) is { Success: true } m
            ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)
            : null;

    internal async Task<PlayingItem?> GetPlayingItemAsync(string ip)
    {
        if (!HasQueue(await ModeOfAsync(ip).ConfigureAwait(false))) return null;
        var index = await QueueCallAsync(ip, "GetQueueIndex", ("QueueName", CurrentQueue)).ConfigureAwait(false);
        if (!int.TryParse(Child(index, "CurrentIndex"), out int current) || current < 1) return null;
        var response = await QueueCallAsync(ip, "BrowseQueue", ("QueueName", CurrentQueue)).ConfigureAwait(false);
        try
        {
            var list = XDocument.Parse(Child(response, "QueueContext")).Root;
            var track = list?.Element("Tracks")?.Elements().ElementAtOrDefault(current - 1);
            string url = track?.Element("URL")?.Value ?? string.Empty;
            if (list == null || url.Length == 0) return null;
            string metadata = track!.Element("Metadata")?.Value ?? string.Empty;
            var item = Didl.Parse(metadata)?.FirstOrDefault();
            string source = list.Element("ListInfo")?.Element("SourceName")?.Value is { Length: > 0 } s ? s : list.Element("ListName")?.Value ?? string.Empty;
            return new PlayingItem(url, metadata, item?.Title is { Length: > 0 } title ? title : url, item?.ArtUrl ?? string.Empty, source);
        }
        catch (Exception) { return null; }
    }

    internal Task<bool> SavePresetAsync(string ip, int number, PlayingItem item, string name) =>
        UpdateKeyAsync(ip, number, _ => MusicKey(item, name), saved => KeyValue(saved, "Url") == item.Url);

    internal Task<bool> RenamePresetAsync(string ip, int number, string name) =>
        UpdateKeyAsync(ip, number, content => IsMusicKey(content) ? RenameKey(content, name) : null, saved => KeyValue(saved, "Name") == name);

    internal Task<bool> ClearPresetAsync(string ip, int number) =>
        UpdateKeyAsync(ip, number, _ => EmptyKey, cleared => !IsMusicKey(cleared) && KeyValue(cleared, "RoutineId") is null or "Empty");
}
