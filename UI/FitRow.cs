using Avalonia;
using Avalonia.Controls;

namespace WiimControl;

sealed class FitRow : Panel
{
    public double Spacing { get; set; } = 8;

    public FitRow() => ClipToBounds = true;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            double needed = child.DesiredSize.Width + (width > 0 ? Spacing : 0);
            if (width + needed > availableSize.Width) break;
            width += needed;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        if (Children.Count > 0 && height == 0) height = Children[0].DesiredSize.Height;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        bool full = false;
        foreach (var child in Children)
        {
            double width = child.DesiredSize.Width;
            full |= x + width > finalSize.Width + 0.5;
            child.Arrange(full
                ? new Rect(finalSize.Width + 10000, 0, width, finalSize.Height)
                : new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }
        return finalSize;
    }
}
