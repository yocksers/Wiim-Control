namespace WiimControl;

sealed partial class WiimController
{
    private bool _tabletMode;

    internal bool TabletMode
    {
        get => _tabletMode;
        set
        {
            _tabletMode = value;
            SaveConfig();
            TouchKeyboard.SetEnabled(value);
        }
    }
}
