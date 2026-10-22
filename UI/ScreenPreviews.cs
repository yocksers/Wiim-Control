using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WiimControl;

static class ScreenPreviews
{
    private const string Folder = "avares://WiimControl/Assets/Screen/";
    private static readonly Dictionary<string, Bitmap?> Loaded = new();
    private static readonly int[] RoundMeterOrder = [1, 3, 2, 4, 5];

    internal const string DigitalClock = "digital-clock";

    internal sealed record SoundClock(int Feature, int ClockView, int Background, int Color, string Picture, string Name);

    internal static readonly SoundClock[] SoundClocks =
    [
        new(7, 3, 0, 0, "bg_clockview_sound", "Analog 1"), new(3, 3, 1, 0, "bg_clockview_sound1", "Analog 2"),
        new(8, 3, 2, 0, "bg_clockview_sound2", "Analog 3"), new(9, 3, 3, 0, "bg_clockview_sound3", "Analog 4"),
        new(10, 3, 4, 0, "bg_clockview_sound4", "Analog 5"), new(11, 3, 5, 0, "bg_clockview_sound5", "Analog 6"),
        new(4, 4, 0, 0, "bg_clockview_sound6", "White"), new(16, 4, 0, 1, "bg_clockview_sound7", "Red"),
        new(17, 4, 0, 2, "bg_clockview_sound8", "Yellow"), new(18, 4, 0, 3, "bg_clockview_sound9", "Green"),
        new(19, 4, 0, 4, "bg_clockview_sound10", "Blue"), new(20, 4, 0, 5, "bg_clockview_sound11", "Purple"),
        new(12, 3, 6, 0, "bg_fire", "Fire"), new(13, 3, 7, 0, "bg_smoke", "Smoke"), new(15, 3, 9, 0, "bg_start", "Stars")
    ];

    private const int DigitalClockView = 4;

    internal static SoundClock? CurrentSoundClock(int clockView, int? background, int? color) =>
        SoundClocks.FirstOrDefault(c => c.ClockView == clockView &&
                                        (clockView == DigitalClockView ? c.Color == (color ?? 0) : c.Background == (background ?? 0)));

    internal static bool IsRoundProject(string? project) =>
        project is { Length: > 0 } && (project.StartsWith("WiiM_Sound", StringComparison.OrdinalIgnoreCase) ||
                                       project.StartsWith("WiiM_Bar", StringComparison.OrdinalIgnoreCase));

    internal static string? Name(string key, int value, bool round, string? input, IReadOnlyDictionary<string, int> current)
    {
        string? name = round ? RoundName(key, value, input, current) : RectName(key, value, input, current);
        return name is null or DigitalClock ? name : name + (round ? ".png" : ".jpg");
    }

    internal static Bitmap? Load(string file)
    {
        lock (Loaded)
        {
            if (Loaded.TryGetValue(file, out var bitmap)) return bitmap;
            try
            {
                var uri = new Uri(Folder + file);
                bitmap = AssetLoader.Exists(uri) ? new Bitmap(AssetLoader.Open(uri)) : null;
            }
            catch (Exception) { bitmap = null; }
            Loaded[file] = bitmap;
            return bitmap;
        }
    }

    private static string? RectInput(string? input) => input?.ToLowerInvariant() switch
    {
        "hdmi" => "hdmi",
        "linein" => "linein",
        "optical" => "optical",
        "phono" => "phono",
        _ => null
    };

    private static string RoundInput(string? input) => input?.ToLowerInvariant() switch
    {
        "bluetooth" => "bt",
        "auxin" => "auxin",
        "hdmi" => "hdmi",
        "optical" => "optical",
        "linein" => "line_in",
        _ => "wifi"
    };

    private static string? RectName(string key, int value, string? input, IReadOnlyDictionary<string, int> current)
    {
        string? source = RectInput(input);
        switch (key)
        {
            case "main_music_view":
                return value switch
                {
                    0 => "bg_playback_screen1", 1 => "bg_playback_screen1_3", 2 => "bg_playback_screen1_2", 3 => "bg_playback_screen1_1", _ => null
                };
            case "sub_cover_view":
                return value switch { 0 => "bg_playback_screen4", 1 => "bg_playback_screen5", 2 => "bg_playback_black_cover", _ => null };
            case "eq_view":
                string? spectrum = value switch
                {
                    0 => "bg_playback_screen_spectrum_cold_normal", 1 => "bg_playback_screen_spectrum_cold_minimalist",
                    2 => "bg_playback_screen_spectrum_warm_normal", 3 => "bg_playback_screen_spectrum_warm_minimalistl", _ => null
                };
                return spectrum != null && source != null && value is 1 or 3 ? $"{spectrum}_{source}" : spectrum;
            case "uvmeter_view":
                return value is >= 0 and <= 4 ? (source != null ? $"vu_{source}_{value + 1}" : $"vu_{value + 1}") : null;
            case "clock_view":
                return value switch
                {
                    0 => "clock_face_center", 1 => "bg_wallpaper_caton", 3 => "bg_wallpaper_anlog", 4 => DigitalClock,
                    5 => "bg_wallpaper_anlog1", _ => null
                };
            case "standby_view":
                return value switch
                {
                    0 => "clock_face_center", 1 => "clock_face_top", 2 => "clock_face_left_bottom", 3 => "clock_face_left_top",
                    7 => "clock_face_right", _ => null
                };
            case "sub_music_view":
                return value switch
                {
                    0 => RectName("sub_cover_view", current.GetValueOrDefault("sub_cover_view"), input, current) ?? "bg_playback_screen4",
                    2 => RectName("eq_view", current.GetValueOrDefault("eq_view"), input, current),
                    3 => RectName("uvmeter_view", current.GetValueOrDefault("uvmeter_view"), input, current),
                    _ => null
                };
            default:
                return null;
        }
    }

    private static string? RoundName(string key, int value, string? input, IReadOnlyDictionary<string, int> current)
    {
        switch (key)
        {
            case "main_music_view":
                return value is >= 7 and <= 11 ? $"bg_playback_screen_sound{value - 3}" : null;
            case "standby_view":
                return value is >= 4 and <= 6 ? $"bg_sound_clock_face{value - 3}" : null;
            case "uvmeter_view":
                return value >= 0 && value < RoundMeterOrder.Length ? $"vu_{RoundInput(input)}_circle_{RoundMeterOrder[value]}" : null;
            case "sub_music_view":
                return value switch
                {
                    3 => RoundName("uvmeter_view", current.GetValueOrDefault("uvmeter_view"), input, current),
                    4 => "bg_playback_screen_sound1",
                    5 => "bg_playback_screen_sound2",
                    6 => "bg_playback_screen_sound3",
                    _ => null
                };
            default:
                return null;
        }
    }
}
