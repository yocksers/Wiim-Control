using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace WiimControl;

sealed class ArtworkView : Border
{
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill, IsVisible = false };
    private readonly PathIcon _placeholder = new()
    {
        Data = Icons.Music, Width = 26, Height = 26, Foreground = Palette.TextMuted,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };

    public ArtworkView()
    {
        Width = 58;
        Height = 58;
        CornerRadius = new CornerRadius(8);
        ClipToBounds = true;
        Background = Palette.Input;
        Margin = new Thickness(0, 0, 14, 0);
        Child = new Panel { Children = { _placeholder, _image } };
    }

    public string ImageUrl { get; set; } = string.Empty;

    public Bitmap? Bitmap
    {
        get => _image.Source as Bitmap;
        set
        {
            _image.Source = value;
            _image.IsVisible = value != null;
            _placeholder.IsVisible = value == null;
        }
    }
}
