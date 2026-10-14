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

sealed record MultiroomSnapshot(HashSet<string> Online, Dictionary<string, string> LeaderOf, Dictionary<string, string> MemberAddresses)
{
    public int MemberCount(string leaderIp) => LeaderOf.Values.Count(v => v == leaderIp);
    public string AddressOf(string ip) => MemberAddresses.TryGetValue(ip, out var address) ? address : ip;
}

readonly record struct DeviceVolume(int Volume, bool Muted);

sealed record DeviceStatus(DeviceVolume Volume, string PlayState, int Mode, string Title, string Artist, string Album,
    string ArtUrl, int SampleRate, int BitDepth, int Loop = 4, int PositionMs = 0, int DurationMs = 0, int BitRate = 0,
    string BluetoothName = "", int SleepSeconds = 0)
{
    public bool IsPlaying => PlayState.Equals("play", StringComparison.OrdinalIgnoreCase);
}

readonly record struct DigitalFilter(int Index, string Name);

sealed record SubwooferInfo(bool Enabled, bool Plugged, double Crossover, double Phase, double Level, bool MainHighPass,
    double? Delay);

sealed record DeviceInfo(string Model, string Firmware, int? CpuTemperature, int? BoardTemperature);

sealed record PresetInfo(int Number, string Name, string Source, string PictureUrl, string Detail = "", bool IsRoutine = false);

sealed record PresetList(int Slots, List<PresetInfo> Presets);

sealed record AlarmInfo(int Slot, bool Enabled, string Trigger, TimeSpan? TimeUtc, string Path);

sealed record ScreenConfig(string RawJson, bool On, bool AutoBrightness, int Brightness);

readonly record struct InputVisibility(string Mode, bool Shown);

sealed record ScreenView(string Key, int Value, List<int> Choices);

sealed record WallpaperInfo(string RawJson, List<string> Current, int MaxNumber);

sealed record ScreenUi(string? InputKey, string? Input, bool? Clock24, bool? StandbyScreenOff, int? StartPage,
    Dictionary<string, ScreenView> Views);

sealed record AmpSettings(int? MaxVolume, double? Balance, bool? Fade, bool? StatusLight, bool? ButtonsLocked,
    int? OutputHardware, string? FilterMode, List<DigitalFilter> Filters, int? Filter, SubwooferInfo? Subwoofer,
    ScreenConfig? Screen, List<InputVisibility>? Inputs, int? VolumeLightMode, ScreenUi? ScreenUi,
    WallpaperInfo? Wallpapers, DeviceInfo? Device, List<AlarmInfo>? Alarms);

readonly record struct KnownDevice(string Name, string Ip, string Uuid)
{
    public override string ToString() => $"{Name} ({Ip})";
}
