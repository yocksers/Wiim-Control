namespace WiimControl;

enum OsdCorner { TopLeft, TopRight, BottomLeft, BottomRight }

readonly record struct EqBand(int Index, string ParamName, int Value);

sealed record EqState(bool Enabled, string Name, string Source, string PluginUri, List<EqBand> Bands);

sealed record EqPresetList(List<string> Custom, List<string> BuiltIn);

sealed record EqSourceMode(string Source, string PluginUri, bool Enabled);

enum PeqFilter { Off = -1, LowShelf = 0, Peak = 1, HighShelf = 2 }

readonly record struct PeqBand(PeqFilter Filter, double Frequency, double Q, double Gain);

sealed record RoomFitState(bool Enabled, string Active, List<string> Profiles, PeqBand[] Stereo, PeqBand[]? Left, PeqBand[]? Right);

sealed record PeqState(bool Enabled, string Name, bool Split, PeqBand[] Stereo, PeqBand[] Left, PeqBand[] Right);

sealed record MultiroomSnapshot(HashSet<string> Online, Dictionary<string, string> LeaderOf, Dictionary<string, string> MemberAddresses,
    Dictionary<string, int>? Channels = null)
{
    public int MemberCount(string leaderIp) => LeaderOf.Values.Count(v => v == leaderIp);
    public string AddressOf(string ip) => MemberAddresses.TryGetValue(ip, out var address) ? address : ip;
}

readonly record struct DeviceVolume(int Volume, bool Muted);

sealed record DeviceStatus(DeviceVolume Volume, string PlayState, int Mode, string Title, string Artist, string Album,
    string ArtUrl, int SampleRate, int BitDepth, int Loop = 4, int PositionMs = 0, int DurationMs = 0, int BitRate = 0,
    string BluetoothName = "", int SleepSeconds = 0, string SourceLabel = "", string Vendor = "")
{
    public bool IsPlaying => PlayState.Equals("play", StringComparison.OrdinalIgnoreCase);
}

readonly record struct DigitalFilter(int Index, string Name);

sealed record SubwooferInfo(bool Enabled, bool Plugged, double Crossover, double Phase, double Level, bool MainHighPass,
    double? Delay, bool Bypass = false);

sealed record DeviceInfo(string Model, string Firmware, int? CpuTemperature, int? BoardTemperature, FirmwareUpdate? Update = null,
    string Ssid = "", string Mac = "", string Uuid = "", string Project = "", int? Color = null, string Colour = "",
    List<(string Part, string Version)>? Components = null, string EthernetMac = "", string BluetoothMac = "");

sealed record NetworkInfo(bool Wired, string WifiName, string Ip, int? Rssi, string ApMac, int? TxRate, int? Snr, int? Frequency,
    int? Channel, bool? FiveGhzOnly, bool? Roaming, bool? Ipv6, string? PreferredDns, int? StandbySeconds, IpConfig? IpConfig = null,
    bool? Internet = null, int? Noise = null, string WifiMac = "");

sealed record IpConfig(bool Manual, string Address, int Prefix, string Gateway, string Dns);

sealed record PresetInfo(int Number, string Name, string Source, string PictureUrl, string Detail = "", bool IsRoutine = false);

sealed record PresetList(int Slots, List<PresetInfo> Presets);

sealed record AlarmInfo(int Slot, bool Enabled, string Trigger, TimeSpan? TimeUtc, string Path);

sealed record ScreenConfig(string RawJson, bool On, bool AutoBrightness, int Brightness, bool? WakeByTouch = null);

readonly record struct InputVisibility(string Mode, bool Shown);

sealed record ScreenView(string Key, int Value, List<int> Choices);

sealed record WallpaperInfo(string RawJson, List<string> Current, int MaxNumber, int Width = 480, int Height = 320);

sealed record ScreenUi(string? InputKey, string? Input, bool? Clock24, bool? StandbyScreenOff, int? StartPage,
    Dictionary<string, ScreenView> Views, List<string>? Inputs = null, string? PlayingInputKey = null, bool? InputScreenOn = null,
    bool? HideCoverIcon = null, Dictionary<int, string>? ClockColors = null, int? ClockBackground = null, int? ClockColor = null,
    int? VuMeterMode = null, bool? HideVuMeterTitle = null);

sealed record AmpSettings(int? MaxVolume, double? Balance, bool? Fade, bool? StatusLight, bool? ButtonsLocked,
    int? OutputHardware, string? FilterMode, List<DigitalFilter> Filters, int? Filter, SubwooferInfo? Subwoofer,
    ScreenConfig? Screen, List<InputVisibility>? Inputs, int? VolumeLightMode, ScreenUi? ScreenUi,
    WallpaperInfo? Wallpapers, DeviceInfo? Device, List<AlarmInfo>? Alarms, SqueezeliteState? Squeezelite = null,
    NetworkInfo? Network = null, int? ButtonVolumeStep = null, int? RemoteVolumeStep = null, List<string>? OutputModes = null,
    string? UsbDacName = null, bool BluetoothOut = false, bool CastOut = false, OutputState? Output = null,
    InputSettings? InputOptions = null, bool? AutoHeadroom = null, double? HeadroomGain = null, int? Channel = null,
    bool? PromptSounds = null, AutoUpdate? AutoUpdate = null, ExperimentalSettings? Experimental = null, List<string>? Formats = null);

sealed record MediaServer(string Name, string Udn, string ContentDirectoryUrl, string IconUrl, bool OnThisComputer = false,
    bool IsLyrion = false, bool IsRoon = false, bool IsShare = false);

sealed record InputsPage(bool Reachable, List<InputVisibility>? Inputs, InputSettings? Options);

sealed record OutputPage(bool Reachable, OutputState? Output, string? OutputName, string? FilterMode, List<DigitalFilter> Filters, int? Filter);

sealed record SharedFolder(string Name, string Host, string Share, string Folder, string User, string Password);

sealed record SqueezeliteState(string State, string ConnectedServer, string DefaultServer, List<string> Found, bool AutoConnect);

sealed record MediaEntry(string Id, string Title, bool IsContainer, int? ChildCount, string Class, string Artist, string Album,
    string ArtUrl, string Url, string ProtocolInfo, TimeSpan Duration)
{
    public bool IsAudio => !IsContainer && Url.Length > 0 &&
        (Class.StartsWith("object.item.audioItem", StringComparison.Ordinal) || ProtocolInfo.Contains(":audio/", StringComparison.OrdinalIgnoreCase));
}

sealed record BrowsePage(List<MediaEntry> Entries, int Total);

sealed record QueueTrack(string Url, string Title, string Artist, string Album, string ArtUrl, TimeSpan Duration, string ProtocolInfo);

sealed record RadioStation(string Name, string Url, string ArtUrl, string Detail, string Uuid = "");

sealed record RadioCountry(string Code, string Name, int Stations);

readonly record struct KnownDevice(string Name, string Ip, string Uuid)
{
    public override string ToString() => $"{Name} ({Ip})";
}
