using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WiimControl;

static class DevicePictures
{
    private const string Folder = "avares://WiimControl/Assets/Devices/";
    private static readonly Dictionary<string, Bitmap?> Loaded = new();

    private static readonly (string Prefix, string File, int Color, string ColorFile)[] ByProject =
    [
        ("WiiM_Amp_Ultra", "icon_ds_amp_ultra_gray.png", 2, "icon_ds_amp_ultra_silver.png"),
        ("WiiM_Amp_Pro", "icon_ds_amp_pro_gray.png", 2, "icon_ds_amp_pro_silver.png"),
        ("WiiM_Amp", "icon_ds_amp_gray.png", 2, "icon_ds_amp_silver.png"),
        ("WiiM_Ultra", "icon_ds_ultra_gray.png", 2, "icon_ds_ultra_silver.png"),
        ("WiiM_Pro", "icon_ds_pro_black.png", -1, ""),
        ("WiiM_Mini", "icon_ds_mini_black.png", -1, ""),
        ("Muzo_Mini", "icon_ds_mini_black.png", -1, ""),
        ("WiiM_Sound_Lite", "icon_ds_sound_lite_black.png", 1, "icon_ds_sound_lite_white.png"),
        ("WiiM_SoundLite", "icon_ds_sound_lite_black.png", 1, "icon_ds_sound_lite_white.png"),
        ("WiiM_Sound", "icon_ds_sound_black.png", 1, "icon_ds_sound_white.png"),
        ("WiiM_Bar", "icon_ds_bar_black.png", -1, ""),
        ("WiiM_Sub", "icon_ds_sub_pro_black.png", 1, "icon_ds_sub_pro_white.png")
    ];

    private static string? AudioProFile(string project, int? color) => project.ToUpperInvariant() switch
    {
        "AUDIOPRO_A10MKII_W" => "icon_ds_a10_light_gray.png",
        "AUDIOPRO_A10MKII_WIIM" => color switch { 0 => "icon_ds_a10_black.png", 6 => "icon_ds_a10_light_gray.png", _ => "icon_ds_a10_dark_gray.png" },
        "AUDIOPRO_C10MKII_W" => "icon_ds_c10_white.png",
        "AUDIOPRO_C10MKII_WIIM" => color == 1 ? "icon_ds_c10_white.png" : "icon_ds_c10_black.png",
        _ => null
    };

    internal static string? FileFor(string? project, string? model, int? color = null)
    {
        if (project is { Length: > 0 } && ByProject.FirstOrDefault(p => project.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase)) is { File: { } file } match)
            return color == match.Color && match.ColorFile.Length > 0 ? match.ColorFile : file;
        if (project is { Length: > 0 } && AudioProFile(project, color) is { } audioPro) return audioPro;
        string name = $"{project} {model}";
        if (name.Contains("A10", StringComparison.OrdinalIgnoreCase)) return "icon_ds_a10_dark_gray.png";
        if (name.Contains("C10", StringComparison.OrdinalIgnoreCase)) return "icon_ds_c10_gray.png";
        return null;
    }

    internal static Bitmap? Load(string? project, string? model, int? color = null)
    {
        if (FileFor(project, model, color) is not { } file) return null;
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
}
