namespace WiimControl;

sealed partial class WiimController
{
    internal static readonly int[] IdleBigPlayerChoices = [0, 30, 60, 120, 180, 240, 300, 600];
    private int _idleBigPlayerSeconds;

    internal int IdleBigPlayerSeconds
    {
        get => _idleBigPlayerSeconds;
        set
        {
            _idleBigPlayerSeconds = IdleBigPlayerChoices.Contains(value) ? value : 0;
            SaveConfig();
        }
    }
}
