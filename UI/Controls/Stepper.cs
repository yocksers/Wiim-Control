using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed class Stepper : StackPanel
{
    private readonly TextBlock _label = new()
    {
        MinWidth = 44, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };
    private int _min = 1, _max = 100, _value = 1;

    public event EventHandler? ValueChanged;

    public Stepper()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        var minus = new Button { Content = "−", Classes = { "pill" }, Padding = new Avalonia.Thickness(14, 6) };
        var plus = new Button { Content = "+", Classes = { "pill" }, Padding = new Avalonia.Thickness(14, 6) };
        minus.Click += (_, _) => Value--;
        plus.Click += (_, _) => Value++;
        _label.Text = _value.ToString();
        Children.Add(minus);
        Children.Add(_label);
        Children.Add(plus);
    }

    public int Minimum { get => _min; set { _min = value; Value = _value; } }
    public int Maximum { get => _max; set { _max = value; Value = _value; } }

    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, _min, _max);
            if (v == _value) return;
            _value = v;
            _label.Text = v.ToString();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
