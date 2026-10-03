namespace WiimControl;

enum OsdCorner { TopLeft, TopRight, BottomLeft, BottomRight }

readonly record struct EqBand(int Index, string ParamName, int Value);

sealed record EqState(bool Enabled, string Name, string Source, string PluginUri, List<EqBand> Bands);

sealed record EqPresetList(List<string> Custom, List<string> BuiltIn);

sealed record MultiroomSnapshot(HashSet<string> Online, Dictionary<string, string> LeaderOf, Dictionary<string, string> MemberAddresses)
{
    public int MemberCount(string leaderIp) => LeaderOf.Values.Count(v => v == leaderIp);
    public string AddressOf(string ip) => MemberAddresses.TryGetValue(ip, out var address) ? address : ip;
}

readonly record struct DeviceVolume(int Volume, bool Muted);

sealed record DeviceStatus(DeviceVolume Volume, string PlayState, int Mode, string Title, string Artist, string Album,
    string ArtUrl, int SampleRate, int BitDepth)
{
    public bool IsPlaying => PlayState.Equals("play", StringComparison.OrdinalIgnoreCase);
}

readonly record struct DigitalFilter(int Index, string Name);

sealed record SubwooferInfo(bool Enabled, bool Plugged, double Crossover, double Phase, double Level, bool MainHighPass);

sealed record ScreenConfig(string RawJson, bool On, bool AutoBrightness, int Brightness);

sealed record AmpSettings(int? MaxVolume, double? Balance, bool? Fade, bool? StatusLight, bool? ButtonsLocked,
    int? OutputHardware, string? FilterMode, List<DigitalFilter> Filters, int? Filter, SubwooferInfo? Subwoofer,
    ScreenConfig? Screen);

readonly record struct KnownDevice(string Name, string Ip, string Uuid)
{
    public override string ToString() => $"{Name} ({Ip})";
}
