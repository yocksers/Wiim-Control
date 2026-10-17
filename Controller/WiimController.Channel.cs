using System.Globalization;

namespace WiimControl;

sealed partial class WiimController
{
    private const string RenderingControlService = "urn:schemas-upnp-org:service:RenderingControl:1";
    internal const int ChannelStereo = 0, ChannelLeft = 1, ChannelRight = 2;

    private Task<string> GetRenderingControlUrlAsync(string ip) =>
        GetServiceUrlAsync(ip, RenderingControlService, "/upnp/control/rendercontrol1");

    internal async Task<int?> GetChannelAsync(string ip)
    {
        var response = await Upnp.CallAsync(await GetRenderingControlUrlAsync(ip).ConfigureAwait(false), RenderingControlService, "GetChannel",
            ("InstanceID", "0"), ("Channel", "Master")).ConfigureAwait(false);
        return int.TryParse(Child(response, "CurrentChannel"), out int channel) ? channel : null;
    }

    internal async Task<bool> SetChannelAsync(string ip, int channel) =>
        await Upnp.CallAsync(await GetRenderingControlUrlAsync(ip).ConfigureAwait(false), RenderingControlService, "SetChannel",
            ("InstanceID", "0"), ("Channel", "Master"), ("DesiredChannel", channel.ToString(CultureInfo.InvariantCulture))).ConfigureAwait(false) != null;
}
