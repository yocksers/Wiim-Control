using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;

namespace WiimControl;

sealed record DeviceProbe(string Name, string Uuid, string Model);

sealed partial class WiimController
{
    internal static string? CleanDeviceAddress(string? input)
    {
        string text = input?.Trim() ?? string.Empty;
        foreach (var scheme in new[] { "https://", "http://" })
            if (text.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) text = text[scheme.Length..];
        int slash = text.IndexOf('/');
        if (slash >= 0) text = text[..slash];
        if (text.Length == 0) return null;
        if (IPAddress.TryParse(text, out var address)) return address.AddressFamily == AddressFamily.InterNetwork && text.Count(c => c == '.') == 3 ? text : null;
        if (text.All(c => char.IsDigit(c) || c == '.')) return null;
        return Uri.CheckHostName(text) == UriHostNameType.Dns ? text : null;
    }

    internal async Task<DeviceProbe?> ProbeDeviceAsync(string address)
    {
        string? statusEx = await SendToDeviceAsync(address, "getStatusEx").ConfigureAwait(false);
        if (JsonField(statusEx, "uuid") is not { } uuid) return null;
        string name = JsonField(statusEx, "DeviceName") is { Length: > 0 } deviceName ? deviceName : string.Empty;
        string model = JsonField(statusEx, "project") is { Length: > 0 } project ? ModelName(project) : string.Empty;
        return new DeviceProbe(name, uuid, model);
    }

    internal async Task<bool> AddDeviceAsync(Window? owner)
    {
        string? typed = await Dialogs.PromptAsync(owner, "Enter the WiiM device's IP address or network name:", string.Empty, UiScale);
        if (string.IsNullOrWhiteSpace(typed)) return false;
        if (CleanDeviceAddress(typed) is not { } address)
        {
            await Dialogs.MessageAsync(owner, $"\"{typed.Trim()}\" isn't an IP address or network name. Enter something like 192.168.1.20.", UiScale);
            return false;
        }
        if (_knownDevices.FirstOrDefault(k => k.Ip.Equals(address, StringComparison.OrdinalIgnoreCase)) is { Ip: not null } existing)
        {
            await Dialogs.MessageAsync(owner, $"{existing.Name} at {address} is already in the list.", UiScale);
            return false;
        }

        var probe = await ProbeDeviceAsync(address);
        if (probe == null)
        {
            if (!await Dialogs.ConfirmAsync(owner, $"Couldn't reach a WiiM device at {address}. Check that it's switched on and on the same " +
                    "network as this computer. Add it anyway?", UiScale)) return false;
        }
        else if (_knownDevices.FirstOrDefault(k => SameDeviceId(k.Uuid, probe.Uuid)) is { Ip: not null } same)
        {
            if (!await Dialogs.ConfirmAsync(owner, $"That's {same.Name}, which is already in the list at {same.Ip}. Change its address to {address}?",
                    UiScale)) return false;
            RelocateDevice(same.Ip, address);
            return true;
        }

        string name = probe?.Name is { Length: > 0 } deviceName ? deviceName : $"WiiM device {_knownDevices.Count + 1}";
        AddManualDevice(address, name, probe?.Uuid ?? string.Empty);
        if (probe?.Model is { Length: > 0 } model) _deviceModels[address] = model;
        return true;
    }
}
