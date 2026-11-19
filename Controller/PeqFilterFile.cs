using System.Globalization;
using System.Text.RegularExpressions;

namespace WiimControl;

sealed record PeqImport(List<PeqBand> Bands, List<string> Skipped, int Adjusted, double? Preamp);

static partial class PeqFilterFile
{
    [GeneratedRegex(@"^\s*Filter\s*(\d*)\s*:\s*(ON|OFF)\b\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex FilterLine();

    [GeneratedRegex(@"^(.*?)\s*\bFc\s+([\d.,]+)\s*Hz(?:\s+Gain\s+([-+]?[\d.,]+)\s*dB)?(?:\s+Q\s+([\d.,]+))?", RegexOptions.IgnoreCase)]
    private static partial Regex FilterValues();

    [GeneratedRegex(@"^\s*Preamp\s*:\s*([-+]?[\d.,]+)\s*dB", RegexOptions.IgnoreCase)]
    private static partial Regex PreampLine();

    public static PeqImport Parse(string text)
    {
        var bands = new List<PeqBand>();
        var skipped = new List<string>();
        var seen = new HashSet<int>();
        int adjusted = 0;
        double? preamp = null;

        foreach (var line in text.Split('\n'))
        {
            if (PreampLine().Match(line) is { Success: true } p && Number(p.Groups[1].Value) is { } gain) preamp = gain;

            var m = FilterLine().Match(line);
            if (!m.Success) continue;
            if (m.Groups[1].Length > 0 && !seen.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))) break;
            if (m.Groups[2].Value.Equals("OFF", StringComparison.OrdinalIgnoreCase)) continue;

            string rest = m.Groups[3].Value.Trim();
            var v = FilterValues().Match(rest);
            string type = (v.Success ? v.Groups[1].Value : rest).Trim();
            if (type.Length == 0 || type.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;

            double? frequency = v.Success ? Number(v.Groups[2].Value) : null;
            double? gainDb = v.Success && v.Groups[3].Success ? Number(v.Groups[3].Value) : null;
            double? q = v.Success && v.Groups[4].Success ? Number(v.Groups[4].Value) : null;
            var filter = FilterOf(type, q != null);
            if (filter == null || frequency == null || gainDb == null || q == null)
            {
                skipped.Add(m.Groups[1].Length > 0 ? $"Filter {m.Groups[1].Value}: {type}" : type);
                continue;
            }

            double f = Math.Clamp(frequency.Value, PeqGraph.MinFrequency, PeqGraph.MaxFrequency);
            double g = Math.Clamp(gainDb.Value, PeqGraph.MinGain, PeqGraph.MaxGain);
            double qc = Math.Clamp(q.Value, PeqGraph.MinQ, PeqGraph.MaxQ);
            if (f != frequency.Value || g != gainDb.Value || qc != q.Value) adjusted++;
            bands.Add(new PeqBand(filter.Value, Math.Round(f, 3), Math.Round(qc, 3), Math.Round(g, 3)));
        }
        return new PeqImport(bands, skipped, adjusted, preamp);
    }

    private static PeqFilter? FilterOf(string type, bool hasQ)
    {
        string t = type.Replace(" ", string.Empty).ToUpperInvariant();
        if (t is "PK" or "PEQ" or "PEAK") return PeqFilter.Peak;
        if (!hasQ) return null;
        if (t is "LSQ" or "LSC" or "LS") return PeqFilter.LowShelf;
        if (t is "HSQ" or "HSC" or "HS") return PeqFilter.HighShelf;
        return null;
    }

    private static double? Number(string text)
    {
        if (!text.Contains('.')) text = text.Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
    }
}
