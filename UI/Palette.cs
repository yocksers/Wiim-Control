using Avalonia.Media;

namespace WiimControl;

static class Palette
{
    public static readonly SolidColorBrush Window      = Brush("#181818");
    public static readonly SolidColorBrush Sidebar     = Brush("#202020");
    public static readonly SolidColorBrush Card        = Brush("#2A2A2A");
    public static readonly SolidColorBrush Input       = Brush("#3A3A3A");
    public static readonly SolidColorBrush Text        = Brush("#FFFFFF");
    public static readonly SolidColorBrush TextMuted   = Brush("#A6A6A6");
    public static readonly SolidColorBrush TextDim     = Brush("#6E6E6E");
    public static readonly SolidColorBrush TextDimmer  = Brush("#7A7A7A");
    public static readonly SolidColorBrush Accent      = Brush("#5B5BF6");
    public static readonly SolidColorBrush AccentHover = Brush("#7272FF");
    public static readonly SolidColorBrush AccentText  = Brush("#A4A4FF");
    public static readonly SolidColorBrush AccentTextHover = Brush("#B4B4FF");
    public static readonly SolidColorBrush OnAccent    = Brush("#FFFFFF");
    public static readonly SolidColorBrush Track       = Brush("#555555");
    public static readonly SolidColorBrush InputHover  = Brush("#4A4A4A");
    public static readonly SolidColorBrush InputPressed = Brush("#555555");
    public static readonly SolidColorBrush InputDisabled = Brush("#303030");
    public static readonly SolidColorBrush ToggleOffHover = Brush("#606060");
    public static readonly SolidColorBrush DropDown    = Brush("#2F2F2F");
    public static readonly SolidColorBrush CardActive  = Brush("#474747");
    public static readonly SolidColorBrush CardGlass   = Brush("#D02A2A2A");
    public static readonly SolidColorBrush CardActiveGlass = Brush("#D0474747");
    public static readonly SolidColorBrush NavHover    = Brush("#2C2C2C");
    public static readonly SolidColorBrush GroupHover  = Brush("#363636");
    public static readonly SolidColorBrush SegmentText = Brush("#D0D0D0");
    public static readonly SolidColorBrush AccentSoft  = Brush("#30305E");
    public static readonly SolidColorBrush AccentSoftHover = Brush("#363668");
    public static readonly SolidColorBrush AccentSoftDisabled = Brush("#2A2A3E");
    public static readonly SolidColorBrush AccentTextDisabled = Brush("#7A7AB0");
    public static readonly SolidColorBrush NavSelected = Brush("#25254A");
    public static readonly SolidColorBrush NavSelectedHover = Brush("#2C2C55");
    public static readonly SolidColorBrush Outline     = Brush("#9A9A9A");
    public static readonly SolidColorBrush OutlineHover = Brush("#C8C8C8");
    public static readonly SolidColorBrush GraphZero   = Brush("#5A5A5A");
    public static readonly SolidColorBrush SidebarGlass = Brush("#A0202020");
    public static readonly SolidColorBrush WindowScrim = Brush("#A4181818");

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
