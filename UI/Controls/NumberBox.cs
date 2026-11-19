using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiimControl;

sealed class NumberBox : TextBox
{
    private readonly Func<double, string> _format;
    private readonly Func<double, int, double> _nudge;
    private readonly double _min, _max;
    private double _value;

    protected override Type StyleKeyOverride => typeof(TextBox);

    public event EventHandler? ValueChanged;

    public NumberBox(double min, double max, Func<double, string> format, Func<double, int, double> nudge)
    {
        _min = min;
        _max = max;
        _format = format;
        _nudge = nudge;
        MinWidth = 0;
        MinHeight = 34;
        Padding = new Avalonia.Thickness(8, 6);
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        TextAlignment = TextAlignment.Center;
        Text = _format(_value);
    }

    public double Value
    {
        get => _value;
        set => SetValue(value, notify: false);
    }

    private void SetValue(double value, bool notify)
    {
        double v = Math.Clamp(value, _min, _max);
        bool changed = v != _value;
        _value = v;
        Text = _format(v);
        if (changed && notify) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Commit()
    {
        if (Text == _format(_value)) return;
        if (TryParse(Text, out double parsed)) SetValue(parsed, notify: true);
        else Text = _format(_value);
    }

    private static bool TryParse(string? text, out double value)
    {
        string s = (text ?? string.Empty).Trim().ToLowerInvariant().Replace(',', '.').Replace(" ", string.Empty);
        double factor = 1;
        if (s.EndsWith("db")) s = s[..^2];
        if (s.EndsWith("hz")) s = s[..^2];
        if (s.EndsWith('k'))
        {
            factor = 1000;
            s = s[..^1];
        }
        bool ok = double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        value *= factor;
        return ok && double.IsFinite(value);
    }

    private void Nudge(int direction)
    {
        Commit();
        SetValue(_nudge(_value, direction), notify: true);
        SelectAll();
    }

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        Commit();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                SelectAll();
                break;
            case Key.Escape when Text != _format(_value):
                Text = _format(_value);
                SelectAll();
                break;
            case Key.Up:
                Nudge(1);
                break;
            case Key.Down:
                Nudge(-1);
                break;
            default:
                base.OnKeyDown(e);
                return;
        }
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0 || !IsEffectivelyEnabled || !IsFocused)
        {
            base.OnPointerWheelChanged(e);
            return;
        }
        Nudge(Math.Sign(e.Delta.Y));
        e.Handled = true;
    }
}
