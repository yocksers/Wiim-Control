namespace WiimControl;

interface IMediaSession : IDisposable
{
    void Update(DeviceStatus? status);
}
