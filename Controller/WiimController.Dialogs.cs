namespace WiimControl;

sealed partial class WiimController
{
    private Task<KnownDevice?> PickOrEnterDeviceAsync(List<KnownDevice> devices) => Dialogs.PickDeviceAsync(devices, UiScale);
}
