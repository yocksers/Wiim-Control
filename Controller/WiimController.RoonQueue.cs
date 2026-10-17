using System.Text.Json;

namespace WiimControl;

sealed partial class WiimController
{
    private int _roonSubscriptionKey;

    private async Task<(string ZoneId, List<JsonElement> Items)?> RoonQueueItemsAsync(string ip)
    {
        if (await RoonZoneIdForCommandAsync(ip).ConfigureAwait(false) is not { } zoneId || _roon is not { } roon) return null;
        int key = Interlocked.Increment(ref _roonSubscriptionKey);
        var reply = await roon.RequestAsync($"{RoonTransportService}/subscribe_queue", new Dictionary<string, object?>
        {
            ["zone_or_output_id"] = zoneId, ["max_item_count"] = MaxQueueTracks, ["subscription_key"] = key
        }, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        _ = roon.RequestAsync($"{RoonTransportService}/unsubscribe_queue", new Dictionary<string, object?> { ["subscription_key"] = key },
            TimeSpan.FromSeconds(5));
        if (reply == null || Prop(reply.Body, "items") is not { ValueKind: JsonValueKind.Array } items) return null;
        return (zoneId, items.EnumerateArray().ToList());
    }

    private static string RoonQueueSignature(List<JsonElement> items) =>
        "roon|" + string.Join(',', items.Select(i => Prop(i, "queue_item_id").ToString()));

    private async Task<QueueState?> RoonQueueStateAsync(string ip)
    {
        if (await RoonQueueItemsAsync(ip).ConfigureAwait(false) is not { } queue) return null;
        return queue.Items.Count == 0
            ? new QueueState(QueueKind.None, -1, "none|roon")
            : new QueueState(QueueKind.Roon, 0, RoonQueueSignature(queue.Items));
    }

    private async Task<QueueView> GetRoonQueueAsync(string ip)
    {
        if (await RoonQueueItemsAsync(ip).ConfigureAwait(false) is not { } queue || queue.Items.Count == 0) return QueueView.Empty;
        var roon = _roon;
        var entries = new List<QueueEntry>();
        var ids = new List<string>();
        foreach (var item in queue.Items)
        {
            var lines = Prop(item, "three_line");
            string image = Prop(item, "image_key").ToString();
            double.TryParse(Prop(item, "length").ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                out double length);
            entries.Add(new QueueEntry(Prop(lines, "line1").ToString(), Prop(lines, "line2").ToString(), Prop(lines, "line3").ToString(),
                image.Length > 0 && roon != null ? $"http://{roon.Host}:{roon.Port}/api/image/{image}?scale=fit&width=120&height=120&format=image/jpeg" : string.Empty,
                TimeSpan.FromSeconds(length)));
            ids.Add(Prop(item, "queue_item_id").ToString());
        }
        return new QueueView(QueueKind.Roon, RoonCoreName, entries, 0, RoonQueueSignature(queue.Items), ids);
    }

    private async Task<bool> RoonPlayFromHereAsync(string ip, QueueView view, int index)
    {
        if (view.ItemIds is not { } ids || index < 0 || index >= ids.Count) return false;
        if (await RoonZoneIdForCommandAsync(ip).ConfigureAwait(false) is not { } zoneId || _roon is not { } roon) return false;
        _roonZones.TryRemove(ip, out _);
        return (await roon.RequestAsync($"{RoonTransportService}/play_from_here", new Dictionary<string, object?>
        {
            ["zone_or_output_id"] = zoneId, ["queue_item_id"] = long.TryParse(ids[index], out long id) ? id : ids[index]
        }).ConfigureAwait(false))?.IsSuccess == true;
    }
}
