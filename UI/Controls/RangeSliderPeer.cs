using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace WiimControl;

interface IRangeSlider
{
    int Minimum { get; }
    int Maximum { get; }
    int Value { get; }
    int SmallStep { get; }
    void SetValueFromAutomation(int value);
}

sealed class RangeSliderPeer(Control owner, IRangeSlider slider) : ControlAutomationPeer(owner), IRangeValueProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    public bool IsReadOnly => !Owner.IsEffectivelyEnabled;

    public double Minimum => slider.Minimum;

    public double Maximum => slider.Maximum;

    public double Value => slider.Value;

    public double SmallChange => slider.SmallStep;

    public double LargeChange => Math.Max(slider.SmallStep, (slider.Maximum - slider.Minimum) / 10);

    public void SetValue(double value) => slider.SetValueFromAutomation((int)Math.Round(value));

    internal void RaiseValueChanged(int oldValue, int newValue) =>
        RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, (double)oldValue, (double)newValue);
}
