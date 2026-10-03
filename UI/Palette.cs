using Avalonia.Media;

namespace WiimControl;

static class Palette
{
    public static readonly IBrush Window      = new SolidColorBrush(Color.Parse("#181818"));
    public static readonly IBrush Sidebar     = new SolidColorBrush(Color.Parse("#202020"));
    public static readonly IBrush Card        = new SolidColorBrush(Color.Parse("#2A2A2A"));
    public static readonly IBrush Input       = new SolidColorBrush(Color.Parse("#3A3A3A"));
    public static readonly IBrush Text        = Brushes.White;
    public static readonly IBrush TextMuted   = new SolidColorBrush(Color.Parse("#A6A6A6"));
    public static readonly IBrush TextDim     = new SolidColorBrush(Color.Parse("#6E6E6E"));
    public static readonly IBrush Accent      = new SolidColorBrush(Color.Parse("#5B5BF6"));
    public static readonly IBrush AccentHover = new SolidColorBrush(Color.Parse("#7272FF"));
    public static readonly IBrush AccentText  = new SolidColorBrush(Color.Parse("#A4A4FF"));
    public static readonly IBrush Track       = new SolidColorBrush(Color.Parse("#555555"));
    public static readonly IBrush Fill        = new SolidColorBrush(Color.Parse("#E6E6E6"));
}
