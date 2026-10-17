using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace WiimControl;

static class Didl
{
    private static readonly XNamespace DidlNs = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace UpnpNs = "urn:schemas-upnp-org:metadata-1-0/upnp/";
    private static readonly XNamespace DlnaNs = "urn:schemas-dlna-org:metadata-1-0/";

    public static List<MediaEntry>? Parse(string didl)
    {
        XDocument doc;
        try { doc = XDocument.Parse(didl); }
        catch (XmlException) { return null; }
        if (doc.Root == null) return null;

        var entries = new List<MediaEntry>();
        foreach (var element in doc.Root.Elements())
        {
            bool container = element.Name == DidlNs + "container";
            if (!container && element.Name != DidlNs + "item") continue;

            string Text(XName name) => element.Element(name)?.Value.Trim() ?? string.Empty;
            var artists = element.Elements(UpnpNs + "artist").ToList();
            string artist = (artists.FirstOrDefault(a => (string?)a.Attribute("role") is null or "Performer")
                             ?? artists.FirstOrDefault())?.Value.Trim() is { Length: > 0 } a ? a : Text(Dc + "creator");
            var art = element.Elements(UpnpNs + "albumArtURI").ToList();
            string artUrl = (art.FirstOrDefault(e => (string?)e.Attribute(DlnaNs + "profileID") == "JPEG_TN") ?? art.FirstOrDefault())
                ?.Value.Trim() ?? string.Empty;

            var resources = element.Elements(DidlNs + "res").ToList();
            var res = resources.FirstOrDefault(r => MimeOf((string?)r.Attribute("protocolInfo")).StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                      ?? resources.FirstOrDefault();
            int? childCount = int.TryParse((string?)element.Attribute("childCount"), out int c) ? c : null;

            entries.Add(new MediaEntry(
                (string?)element.Attribute("id") ?? string.Empty,
                Text(Dc + "title"),
                container,
                childCount,
                Text(UpnpNs + "class"),
                artist,
                Text(UpnpNs + "album"),
                artUrl,
                res?.Value.Trim() ?? string.Empty,
                (string?)res?.Attribute("protocolInfo") ?? string.Empty,
                ParseDuration((string?)res?.Attribute("duration"))));
        }
        return entries;
    }

    private static string MimeOf(string? protocolInfo) =>
        protocolInfo?.Split(':') is { Length: >= 3 } parts ? parts[2] : string.Empty;

    private static TimeSpan ParseDuration(string? text)
    {
        if (string.IsNullOrEmpty(text)) return TimeSpan.Zero;
        var parts = text.Split(':');
        if (parts.Length != 3 || !int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double s)) return TimeSpan.Zero;
        return new TimeSpan(h, m, 0) + TimeSpan.FromSeconds(s);
    }

    private static string FormatDuration(TimeSpan duration) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}.{duration.Milliseconds:000}");

    public static string TrackMetadata(QueueTrack track, int id, bool broadcast)
    {
        var item = new XElement(DidlNs + "item",
            new XAttribute("id", id), new XAttribute("parentID", ""), new XAttribute("restricted", "1"),
            new XElement(Dc + "title", track.Title),
            new XElement(Dc + "creator", track.Artist),
            new XElement(UpnpNs + "artist", track.Artist),
            new XElement(UpnpNs + "album", track.Album),
            track.ArtUrl.Length > 0 ? new XElement(UpnpNs + "albumArtURI", track.ArtUrl) : null,
            new XElement(UpnpNs + "class", broadcast ? "object.item.audioItem.audioBroadcast" : "object.item.audioItem.musicTrack"),
            new XElement(DidlNs + "res",
                new XAttribute("protocolInfo", track.ProtocolInfo.Length > 0 ? track.ProtocolInfo : "http-get:*:audio/mpeg:*"),
                track.Duration > TimeSpan.Zero ? new XAttribute("duration", FormatDuration(track.Duration)) : null,
                track.Url));
        var root = new XElement(DidlNs + "DIDL-Lite",
            new XAttribute(XNamespace.Xmlns + "dc", Dc), new XAttribute(XNamespace.Xmlns + "upnp", UpnpNs), item);
        return root.ToString(SaveOptions.DisableFormatting);
    }

    public static string QueueContext(string name, IReadOnlyList<QueueTrack> tracks, int startIndex, bool radio, string source = "UPnPServer")
    {
        var list = new XElement("PlayList",
            new XElement("ListName", name),
            new XElement("ListInfo",
                radio ? new XElement("Radio", 1) : null,
                new XElement("SourceName", source),
                new XElement("TrackNumber", tracks.Count),
                new XElement("SearchUrl", ""),
                new XElement("Quality", 0),
                new XElement("UpdateTime", 0),
                new XElement("LastPlayIndex", startIndex + 1),
                new XElement("SwitchPageMode", 0),
                new XElement("CurrentPage", 0),
                new XElement("TotalPages", 0)),
            new XElement("Tracks", tracks.Select((t, i) => new XElement($"Track{i + 1}",
                new XElement("URL", t.Url),
                new XElement("Metadata", TrackMetadata(t, i + 1, radio)),
                new XElement("Id", i + 1)))));
        return "<?xml version=\"1.0\"?>" + list.ToString(SaveOptions.DisableFormatting);
    }
}
