using System.Text.Json;

namespace WiimControl;

sealed partial class WiimController
{
    private const string RoonBrowseService = "com.roonlabs.browse:1";
    private const string RoonTransportService = "com.roonlabs.transport:2";
    internal const char RoonInputSeparator = '\u0001';
    internal const string RoonPlayNow = "Play Now", RoonAddNext = "Add Next", RoonQueue = "Queue", RoonStartRadio = "Start Radio";
    private static readonly string[] RoonHiddenRootItems = ["Settings"];

    private readonly SemaphoreSlim _roonBrowseGate = new(1, 1);
    private readonly List<(string Key, string Title)> _roonSession = [];
    private bool _roonSessionValid;

    private Dictionary<string, object?> RoonArgs(string? zone, params (string Name, object? Value)[] values)
    {
        var args = new Dictionary<string, object?> { ["hierarchy"] = "browse" };
        if (zone != null) args["zone_or_output_id"] = zone;
        foreach (var (name, value) in values) args[name] = value;
        return args;
    }

    private static (string Key, string? Input) SplitRoonId(string id) =>
        id.IndexOf(RoonInputSeparator) is int i and >= 0 ? (id[..i], id[(i + 1)..]) : (id, null);

    private static string TitleOf((string Id, string Title) step) => step.Title;

    private async Task<RoonMessage?> RoonBrowseStepAsync(RoonConnection roon, string? zone, string id)
    {
        var (key, input) = SplitRoonId(id);
        var args = RoonArgs(zone, ("item_key", key));
        if (input != null) args["input"] = input;
        return await roon.RequestAsync($"{RoonBrowseService}/browse", args).ConfigureAwait(false);
    }

    private async Task<List<JsonElement>> RoonLoadAsync(RoonConnection roon, int level, int offset, int count)
    {
        var load = await roon.RequestAsync($"{RoonBrowseService}/load",
            RoonArgs(null, ("level", level), ("offset", offset), ("count", count))).ConfigureAwait(false);
        return load is { IsSuccess: true } && Prop(load.Body, "items") is { ValueKind: JsonValueKind.Array } items
            ? items.EnumerateArray().ToList()
            : [];
    }

    private async Task<bool> RoonNavigateAsync(RoonConnection roon, string? zone, IReadOnlyList<(string Id, string Title)> path)
    {
        var titles = path.Select(TitleOf).ToList();
        var session = _roonSession.Select(s => s.Title).ToList();
        if (_roonSessionValid)
        {
            if (titles.SequenceEqual(session)) return true;
            if (titles.Count < session.Count && session.Take(titles.Count).SequenceEqual(titles))
            {
                var pop = await roon.RequestAsync($"{RoonBrowseService}/browse",
                    RoonArgs(zone, ("pop_levels", session.Count - titles.Count))).ConfigureAwait(false);
                if (pop is { IsSuccess: true })
                {
                    _roonSession.RemoveRange(titles.Count, session.Count - titles.Count);
                    return true;
                }
            }
            else if (titles.Count == session.Count + 1 && titles.Take(session.Count).SequenceEqual(session))
            {
                var step = await RoonBrowseStepAsync(roon, zone, path[^1].Id).ConfigureAwait(false);
                if (step is { IsSuccess: true } && Prop(step.Body, "action").ToString() == "list")
                {
                    _roonSession.Add((path[^1].Id, path[^1].Title));
                    return true;
                }
            }
        }
        return await RoonReplayAsync(roon, zone, path).ConfigureAwait(false);
    }

    private async Task<bool> RoonReplayAsync(RoonConnection roon, string? zone, IReadOnlyList<(string Id, string Title)> path)
    {
        _roonSessionValid = false;
        _roonSession.Clear();
        var root = await roon.RequestAsync($"{RoonBrowseService}/browse", RoonArgs(zone, ("pop_all", true))).ConfigureAwait(false);
        if (root is not { IsSuccess: true }) return false;
        for (int level = 0; level < path.Count; level++)
        {
            var (_, input) = SplitRoonId(path[level].Id);
            string? key = null;
            for (int offset = 0; key == null && offset < 5000; offset += 500)
            {
                var items = await RoonLoadAsync(roon, level, offset, 500).ConfigureAwait(false);
                if (items.Count == 0) break;
                key = items.Where(i => Prop(i, "title").ToString() == path[level].Title)
                    .Select(i => Prop(i, "item_key").ToString()).FirstOrDefault();
            }
            if (key == null && input != null)
            {
                string bare = path[level].Title.EndsWith($": {input}", StringComparison.Ordinal)
                    ? path[level].Title[..^(input.Length + 2)]
                    : path[level].Title;
                var items = await RoonLoadAsync(roon, level, 0, 500).ConfigureAwait(false);
                key = items.Where(i => Prop(i, "title").ToString() == bare).Select(i => Prop(i, "item_key").ToString()).FirstOrDefault();
            }
            if (key == null) return false;
            string id = input == null ? key : $"{key}{RoonInputSeparator}{input}";
            var step = await RoonBrowseStepAsync(roon, zone, id).ConfigureAwait(false);
            if (step is not { IsSuccess: true } || Prop(step.Body, "action").ToString() != "list") return false;
            _roonSession.Add((id, path[level].Title));
        }
        _roonSessionValid = true;
        return true;
    }

    internal async Task<BrowsePage?> RoonBrowseAsync(MediaServer server, IReadOnlyList<(string Id, string Title)> path, int start, int count)
    {
        if (await GetRegisteredRoonAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false) is not { } roon) return null;
        await _roonBrowseGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!await RoonNavigateAsync(roon, null, path).ConfigureAwait(false)) return null;
            _roonSessionValid = true;
            var list = await roon.RequestAsync($"{RoonBrowseService}/load",
                RoonArgs(null, ("level", path.Count), ("offset", start), ("count", count))).ConfigureAwait(false);
            if (list is not { IsSuccess: true }) return null;
            int total = int.TryParse(Prop(Prop(list.Body, "list"), "count").ToString(), out int t) ? t : start;
            var entries = new List<MediaEntry>();
            if (Prop(list.Body, "items") is { ValueKind: JsonValueKind.Array } items)
                foreach (var item in items.EnumerateArray())
                {
                    string title = Prop(item, "title").ToString();
                    if (path.Count == 0 && RoonHiddenRootItems.Contains(title))
                    {
                        total--;
                        continue;
                    }
                    if (ToRoonEntry(roon, item) is { } entry) entries.Add(entry);
                }
            return new BrowsePage(entries, total);
        }
        finally
        {
            _roonBrowseGate.Release();
        }
    }

    private static MediaEntry? ToRoonEntry(RoonConnection roon, JsonElement item)
    {
        string hint = Prop(item, "hint").ToString();
        if (hint == "header") return null;
        string key = Prop(item, "item_key").ToString();
        if (key.Length == 0) return null;
        bool prompt = Prop(item, "input_prompt").ValueKind == JsonValueKind.Object;
        string image = Prop(item, "image_key").ToString();
        string art = image.Length > 0 ? $"http://{roon.Host}:{roon.Port}/api/image/{image}?scale=fit&width=120&height=120&format=image/jpeg" : string.Empty;
        bool container = hint == "list" || prompt || hint.Length == 0;
        string cls = container
            ? prompt ? "object.container.roon.input" : "object.container.roon"
            : $"object.item.audioItem.roon.{hint}";
        return new MediaEntry(key, Prop(item, "title").ToString(), container, null, cls, Prop(item, "subtitle").ToString(),
            string.Empty, art, "roon", string.Empty, TimeSpan.Zero);
    }

    internal static bool IsRoonInput(MediaEntry entry) => entry.Class == "object.container.roon.input";

    internal async Task<string?> RoonZoneForAsync(string deviceName)
    {
        if (await GetRegisteredRoonAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false) is not { } roon) return null;
        var zones = await roon.RequestAsync($"{RoonTransportService}/get_zones").ConfigureAwait(false);
        if (zones is not { IsSuccess: true } || Prop(zones.Body, "zones") is not { ValueKind: JsonValueKind.Array } list) return null;
        string? byOutput = null;
        foreach (var zone in list.EnumerateArray())
        {
            if (string.Equals(Prop(zone, "display_name").ToString(), deviceName, StringComparison.OrdinalIgnoreCase))
                return Prop(zone, "zone_id").ToString();
            if (Prop(zone, "outputs") is { ValueKind: JsonValueKind.Array } outputs &&
                outputs.EnumerateArray().Any(o => string.Equals(Prop(o, "display_name").ToString(), deviceName, StringComparison.OrdinalIgnoreCase)))
                byOutput ??= Prop(zone, "zone_id").ToString();
        }
        return byOutput;
    }

    internal async Task<string?> RoonActionAsync(IReadOnlyList<(string Id, string Title)> path, MediaEntry entry, string zone, string action)
    {
        if (await GetRegisteredRoonAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false) is not { } roon) return "Wiim Control isn't connected to Roon.";
        await _roonBrowseGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var target = (Id: entry.Id, Title: entry.Title);
            var levelPath = path.ToList();
            if (entry.IsContainer)
            {
                levelPath.Add(target);
                if (!await RoonNavigateAsync(roon, zone, levelPath).ConfigureAwait(false)) return $"Couldn't open {entry.Title} in Roon.";
                var items = await RoonLoadAsync(roon, levelPath.Count, 0, 100).ConfigureAwait(false);
                var play = items.FirstOrDefault(i => Prop(i, "hint").ToString() == "action_list" &&
                                                     Prop(i, "title").ToString().StartsWith("Play", StringComparison.OrdinalIgnoreCase));
                if (play.ValueKind == JsonValueKind.Undefined) return $"Roon has nothing to play for {entry.Title}. Open it to pick something.";
                target = (Prop(play, "item_key").ToString(), Prop(play, "title").ToString());
            }
            else if (!await RoonNavigateAsync(roon, zone, levelPath).ConfigureAwait(false)) return "Couldn't find this in Roon any more. Go back and open it again.";

            var actions = SplitRoonId(target.Id).Key.Length > 0 ? await RoonBrowseStepAsync(roon, zone, target.Id).ConfigureAwait(false) : null;
            if (actions is not { IsSuccess: true })
            {
                _roonSessionValid = false;
                if (!await RoonReplayAsync(roon, zone, entry.IsContainer ? [.. path, (entry.Id, entry.Title)] : path).ConfigureAwait(false))
                    return "Couldn't find this in Roon any more. Go back and open it again.";
                var level = await RoonLoadAsync(roon, _roonSession.Count, 0, 500).ConfigureAwait(false);
                string? fresh = level.Where(i => Prop(i, "title").ToString() == target.Title).Select(i => Prop(i, "item_key").ToString()).FirstOrDefault();
                if (fresh == null) return "Couldn't find this in Roon any more. Go back and open it again.";
                actions = await RoonBrowseStepAsync(roon, zone, fresh).ConfigureAwait(false);
            }
            _roonSessionValid = false;
            if (actions is not { IsSuccess: true }) return "Roon didn't accept the request.";
            string kind = Prop(actions.Body, "action").ToString();
            if (kind == "message") return Prop(actions.Body, "message").ToString();
            if (kind != "list") return null;

            int actionLevel = int.TryParse(Prop(Prop(actions.Body, "list"), "level").ToString(), out int l) ? l : _roonSession.Count + 1;
            var choices = await RoonLoadAsync(roon, actionLevel, 0, 50).ConfigureAwait(false);
            var chosen = choices.FirstOrDefault(i => Prop(i, "title").ToString() == action);
            if (chosen.ValueKind == JsonValueKind.Undefined)
                chosen = action == RoonPlayNow ? choices.FirstOrDefault(i => Prop(i, "hint").ToString() == "action") : default;
            if (chosen.ValueKind == JsonValueKind.Undefined) return $"Roon doesn't offer “{action}” for {entry.Title}.";
            var done = await RoonBrowseStepAsync(roon, zone, Prop(chosen, "item_key").ToString()).ConfigureAwait(false);
            if (done is not { IsSuccess: true }) return "Roon didn't accept the request.";
            return Prop(done.Body, "action").ToString() == "message" && Prop(done.Body, "is_error").ValueKind == JsonValueKind.True
                ? Prop(done.Body, "message").ToString()
                : null;
        }
        finally
        {
            _roonBrowseGate.Release();
        }
    }
}
