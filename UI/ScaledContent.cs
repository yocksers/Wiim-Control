using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WiimControl;

sealed class ScaledContent : Decorator
{
    private double _scale = 1;

    public double Scale
    {
        get => _scale;
        set
        {
            _scale = value;
            ApplyTransform();
            InvalidateMeasure();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChildProperty) ApplyTransform();
    }

    private void ApplyTransform()
    {
        if (Child == null) return;
        Child.RenderTransformOrigin = RelativePoint.TopLeft;
        Child.RenderTransform = new ScaleTransform(_scale, _scale);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child == null) return default;
        Child.Measure(new Size(availableSize.Width / _scale, availableSize.Height / _scale));
        return new Size(Child.DesiredSize.Width * _scale, Child.DesiredSize.Height * _scale);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, finalSize.Width / _scale, finalSize.Height / _scale));
        return finalSize;
    }
}
