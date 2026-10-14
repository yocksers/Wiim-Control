using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WiimControl;

static class RadioDirectory
{
    private const string Api = "https://all.api.radio-browser.info/json/";
    private const string RadioParadiseArt = "https://radioparadise.com/apple-touch-icon.png";
    public const int PageSize = 100;

    private static readonly HttpClient Http = CreateClient();
    private static List<RadioCountry>? _countries;

    public static readonly RadioStation[] RadioParadise =
    [
        new("Radio Paradise Main Mix", "http://stream.radioparadise.com/aac-320", RadioParadiseArt, "Eclectic mix · AAC 320 kbps"),
        new("Radio Paradise Mellow Mix", "http://stream.radioparadise.com/mellow-320", RadioParadiseArt, "Mellow mix · AAC 320 kbps"),
        new("Radio Paradise Rock Mix", "http://stream.radioparadise.com/rock-320", RadioParadiseArt, "Rock mix · AAC 320 kbps"),
        new("Radio Paradise Global Mix", "http://stream.radioparadise.com/global-320", RadioParadiseArt, "World music · AAC 320 kbps")
    ];

    public static readonly (string Label, string Tag)[] Genres =
    [
        ("Pop", "pop"), ("Rock", "rock"), ("News", "news"), ("Talk", "talk"), ("Hits", "hits"), ("Top 40", "top 40"),
        ("Dance", "dance"), ("Electronic", "electronic"), ("Hip hop", "hip hop"), ("Jazz", "jazz"), ("Blues", "blues"),
        ("Soul", "soul"), ("Classical", "classical"), ("Country", "country"), ("Folk", "folk"), ("Oldies", "oldies"),
        ("80s", "80s"), ("90s", "90s"), ("Alternative", "alternative"), ("Indie", "indie"), ("Metal", "metal"),
        ("Chillout", "chillout"), ("Lounge", "lounge"), ("Ambient", "ambient"), ("Reggae", "reggae"), ("Sports", "sports"),
        ("Religious", "religious"), ("Kids", "kids"), ("Comedy", "comedy")
    ];

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"WiimControl/{typeof(RadioDirectory).Assembly.GetName().Version?.ToString(2) ?? "1"}");
        return client;
    }

    private static async Task<string> GetAsync(string url)
    {
        try { return await Http.GetStringAsync(url).ConfigureAwait(false); }
        catch (Exception) { return await Http.GetStringAsync(url).ConfigureAwait(false); }
    }

    public static string HomeCountry()
    {
#if WINDOWS
        try
        {
            if (Windows.System.UserProfile.GlobalizationPreferences.HomeGeographicRegion is { Length: 2 } region) return region.ToUpperInvariant();
        }
        catch (Exception) { }
#endif
        try { return RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant(); }
        catch (Exception) { return "US"; }
    }

    public static string CountryName(string code, string fallback = "")
    {
        try { return new RegionInfo(code).EnglishName; }
        catch (ArgumentException) { return fallback.Length > 0 ? fallback : code; }
    }

    public static async Task<List<RadioStation>?> SearchAsync(string? name = null, string? tag = null, string? country = null, int offset = 0)
    {
        var url = new StringBuilder(Api + "stations/search?order=votes&reverse=true&hidebroken=true")
            .Append(CultureInfo.InvariantCulture, $"&limit={PageSize}&offset={offset}");
        if (!string.IsNullOrWhiteSpace(name)) url.Append("&name=").Append(Uri.EscapeDataString(name.Trim()));
        if (!string.IsNullOrWhiteSpace(tag)) url.Append("&tagExact=true&tag=").Append(Uri.EscapeDataString(tag));
        if (!string.IsNullOrWhiteSpace(country)) url.Append("&countrycode=").Append(Uri.EscapeDataString(country));
        try
        {
            using var doc = JsonDocument.Parse(await GetAsync(url.ToString()).ConfigureAwait(false));
            var stations = new List<RadioStation>();
            foreach (var s in doc.RootElement.EnumerateArray())
            {
                string Str(string field) => s.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()!.Trim() : string.Empty;
                string stream = Str("url_resolved") is { Length: > 0 } resolved ? resolved : Str("url");
                if (stream.Length == 0) continue;
                int bitrate = s.TryGetProperty("bitrate", out var b) && b.TryGetInt32(out int kbps) ? kbps : 0;
                string code = Str("countrycode");
                var detail = new[]
                {
                    code.Length == 2 ? CountryName(code, Str("country")) : Str("country"),
                    string.Join(", ", Str("tags").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().Take(3)),
                    Str("codec") + (bitrate > 0 ? string.Create(CultureInfo.InvariantCulture, $" {bitrate} kbps") : string.Empty)
                }.Where(p => p.Trim().Length > 0);
                stations.Add(new RadioStation(Str("name"), stream, Str("favicon"), string.Join(" · ", detail), Str("stationuuid")));
            }
            return stations;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<List<RadioCountry>?> GetCountriesAsync()
    {
        if (_countries != null) return _countries;
        try
        {
            using var doc = JsonDocument.Parse(await GetAsync(Api + "countries?hidebroken=true").ConfigureAwait(false));
            var countries = new Dictionary<string, RadioCountry>();
            foreach (var c in doc.RootElement.EnumerateArray())
            {
                string code = c.TryGetProperty("iso_3166_1", out var i) ? i.GetString()?.Trim().ToUpperInvariant() ?? "" : "";
                int count = c.TryGetProperty("stationcount", out var n) && n.TryGetInt32(out int v) ? v : 0;
                if (code.Length != 2 || count < 3) continue;
                string name = CountryName(code, c.TryGetProperty("name", out var nm) ? nm.GetString() ?? code : code);
                countries[code] = countries.TryGetValue(code, out var existing)
                    ? existing with { Stations = existing.Stations + count }
                    : new RadioCountry(code, name, count);
            }
            _countries = countries.Values.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            return _countries;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task CountClickAsync(string uuid)
    {
        if (uuid.Length == 0) return;
        try { await Http.GetStringAsync(Api + "url/" + Uri.EscapeDataString(uuid)).ConfigureAwait(false); }
        catch (Exception) { }
    }
}
