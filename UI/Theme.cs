using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace WiimControl;

sealed record Theme(string Name, string Window, string Sidebar, string Card, string Control, string Text, string Muted, string Accent,
    int Roundness = 100)
{
    [JsonIgnore] public bool BuiltIn => ThemeEngine.BuiltIn.Any(t => t.Name == Name);
}

static class ThemeEngine
{
    internal const int MinRoundness = 0;
    internal const int MaxRoundness = 150;

    internal static readonly Theme Dark = new("Dark", "#181818", "#202020", "#2A2A2A", "#3A3A3A", "#FFFFFF", "#A6A6A6", "#5B5BF6");

    internal static readonly Theme[] BuiltIn =
    [
        Dark,
        new("Black", "#000000", "#0A0A0A", "#161616", "#262626", "#FFFFFF", "#A0A0A0", "#5B5BF6"),
        new("Ocean", "#0E1620", "#121C28", "#1A2634", "#253547", "#FFFFFF", "#9DB0C6", "#2E9BFF"),
        new("Forest", "#121815", "#161E1A", "#1E2823", "#2B3831", "#FFFFFF", "#A2B5A9", "#3FBF7F"),
        new("Ember", "#1A1412", "#211915", "#2B211C", "#3B2D26", "#FFFFFF", "#B8A79D", "#F27A3A"),
        new("Light", "#F2F2F5", "#E8E8EC", "#FFFFFF", "#E2E2E8", "#1A1A1A", "#5C5C66", "#4F4FE0", 100)
    ];

    private static readonly (string Key, double Radius)[] Radii =
    [
        ("Wc.RadiusCard", 14), ("Wc.RadiusPill", 20), ("Wc.RadiusChannel", 17), ("Wc.RadiusField", 8), ("Wc.RadiusGroup", 12),
        ("Wc.RadiusNav", 8), ("Wc.RadiusSegments", 14), ("Wc.RadiusSegment", 10)
    ];

    internal static Theme Current { get; private set; } = Dark;

    internal static event Action? Changed;

    internal static Color ColorOf(string hex, Color fallback) => Color.TryParse(hex, out var c) ? c : fallback;

    internal static Color Mix(Color from, Color to, double amount) => Color.FromArgb(255,
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    internal static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    internal static bool IsLight(Theme theme) => Luminance(ColorOf(theme.Window, Colors.Black)) > 0.5;

    internal static void Register(IResourceDictionary resources)
    {
        foreach (var field in typeof(Palette).GetFields())
            if (field.GetValue(null) is SolidColorBrush brush) resources["Wc." + field.Name] = brush;
        resources["ComboBoxDropDownBackground"] = Palette.DropDown;
        resources["ComboBoxDropDownBorderBrush"] = Palette.Input;
        resources["ToggleSwitchFillOff"] = Palette.Track;
        resources["ToggleSwitchFillOffPointerOver"] = Palette.ToggleOffHover;
        resources["ToggleSwitchFillOn"] = Palette.Accent;
        resources["ToggleSwitchFillOnPointerOver"] = Palette.AccentHover;
        resources["ToggleSwitchFillOnPressed"] = Palette.AccentHover;
        resources["ToggleSwitchKnobFillOn"] = Palette.OnAccent;
        resources["ToggleSwitchKnobFillOnPointerOver"] = Palette.OnAccent;
        resources["ToggleSwitchKnobFillOnPressed"] = Palette.OnAccent;
        Apply(Current, resources);
    }

    internal static void Apply(Theme theme) => Apply(theme, Application.Current?.Resources);

    private static void Apply(Theme theme, IResourceDictionary? resources)
    {
        Current = theme;
        var window = ColorOf(theme.Window, Colors.Black);
        var sidebar = ColorOf(theme.Sidebar, window);
        var card = ColorOf(theme.Card, window);
        var control = ColorOf(theme.Control, card);
        var text = ColorOf(theme.Text, Colors.White);
        var muted = ColorOf(theme.Muted, Mix(window, text, 0.62));
        var accent = ColorOf(theme.Accent, Color.Parse("#5B5BF6"));
        var accentText = Mix(accent, text, 0.45);

        Palette.Window.Color = window;
        Palette.Sidebar.Color = sidebar;
        Palette.Card.Color = card;
        Palette.Input.Color = control;
        Palette.Text.Color = text;
        Palette.TextMuted.Color = muted;
        Palette.TextDim.Color = Mix(window, text, 0.37);
        Palette.TextDimmer.Color = Mix(window, text, 0.42);
        Palette.Accent.Color = accent;
        Palette.AccentHover.Color = Mix(accent, text, 0.14);
        Palette.AccentText.Color = accentText;
        Palette.AccentTextHover.Color = Mix(accent, text, 0.55);
        Palette.OnAccent.Color = Luminance(accent) > 0.6 ? Colors.Black : Colors.White;
        Palette.Track.Color = Mix(control, text, 0.13);
        Palette.InputHover.Color = Mix(control, text, 0.08);
        Palette.InputPressed.Color = Mix(control, text, 0.13);
        Palette.InputDisabled.Color = Mix(card, control, 0.375);
        Palette.ToggleOffHover.Color = Mix(control, text, 0.19);
        Palette.DropDown.Color = Mix(card, control, 0.3);
        Palette.CardActive.Color = Mix(card, text, 0.135);
        Palette.CardGlass.Color = WithAlpha(card, 0xD0);
        Palette.CardActiveGlass.Color = WithAlpha(Mix(card, text, 0.135), 0xD0);
        Palette.NavHover.Color = Mix(sidebar, text, 0.054);
        Palette.GroupHover.Color = Mix(card, text, 0.056);
        Palette.SegmentText.Color = Mix(text, muted, 0.53);
        Palette.AccentSoft.Color = Mix(card, accent, 0.2);
        Palette.AccentSoftHover.Color = Mix(card, accent, 0.25);
        Palette.AccentSoftDisabled.Color = Mix(card, accent, 0.08);
        Palette.AccentTextDisabled.Color = Mix(card, accentText, 0.6);
        Palette.NavSelected.Color = Mix(sidebar, accent, 0.15);
        Palette.NavSelectedHover.Color = Mix(sidebar, accent, 0.2);
        Palette.Outline.Color = Mix(window, text, 0.57);
        Palette.OutlineHover.Color = Mix(window, text, 0.76);
        Palette.GraphZero.Color = Mix(control, text, 0.16);
        Palette.SidebarGlass.Color = WithAlpha(sidebar, 0xA0);
        Palette.WindowScrim.Color = WithAlpha(window, 0xA4);

        if (resources != null)
        {
            resources["SystemAccentColor"] = accent;
            resources["SystemAccentColorDark1"] = Mix(accent, Colors.Black, 0.08);
            resources["SystemAccentColorDark2"] = Mix(accent, Colors.Black, 0.16);
            resources["SystemAccentColorDark3"] = Mix(accent, Colors.Black, 0.24);
            resources["SystemAccentColorLight1"] = Mix(accent, Colors.White, 0.14);
            resources["SystemAccentColorLight2"] = Mix(accent, Colors.White, 0.24);
            resources["SystemAccentColorLight3"] = Mix(accent, Colors.White, 0.45);
            double scale = Math.Clamp(theme.Roundness, MinRoundness, MaxRoundness) / 100.0;
            foreach (var (key, radius) in Radii) resources[key] = new CornerRadius(radius * scale);
        }
        if (Application.Current is { } app) app.RequestedThemeVariant = IsLight(theme) ? ThemeVariant.Light : ThemeVariant.Dark;
        Redraw();
        Changed?.Invoke();
    }

    private static void Redraw()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        foreach (var window in desktop.Windows)
            foreach (var visual in window.GetVisualDescendants().Prepend(window)) visual.InvalidateVisual();
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    internal static string Serialize(IEnumerable<Theme> themes) => JsonSerializer.Serialize(themes.ToList(), Json);

    internal static List<Theme> ParseThemes(string? json)
    {
        try { return JsonSerializer.Deserialize<List<Theme>>(json ?? string.Empty, Json)?.Where(t => t.Name.Length > 0).ToList() ?? []; }
        catch (JsonException) { return []; }
    }

    internal static string Serialize(Theme theme) => JsonSerializer.Serialize(theme, Json);

    internal static Theme? ParseTheme(string? json)
    {
        try { return JsonSerializer.Deserialize<Theme>(json ?? string.Empty, Json); }
        catch (JsonException) { return null; }
    }
}
