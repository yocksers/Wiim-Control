using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace WiimControl;

sealed class GStreamerLocalPlayer : ILocalPlayer
{
    private const string Gst = "libgstreamer-1.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const int StateNull = 1, StatePaused = 3, StatePlaying = 4;
    private const int FormatTime = 3;
    private const int SeekFlush = 1, SeekKeyUnit = 4;
    private const int MessageEos = 1, MessageError = 2;
    private const nuint TypeBoolean = 5 << 2, TypeDouble = 15 << 2, TypeString = 16 << 2;
    private const long NanosPerTick = 100;

    [StructLayout(LayoutKind.Sequential)]
    private struct GValue
    {
        public nuint Type;
        public long Data0, Data1;
    }

    [DllImport(Gst)] private static extern int gst_init_check(nint argc, nint argv, out nint error);
    [DllImport(Gst)] private static extern nint gst_element_factory_make([MarshalAs(UnmanagedType.LPUTF8Str)] string factory, nint name);
    [DllImport(Gst)] private static extern nint gst_element_factory_find([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Gst)] private static extern int gst_element_set_state(nint element, int state);
    [DllImport(Gst)] private static extern int gst_element_query_position(nint element, int format, out long position);
    [DllImport(Gst)] private static extern int gst_element_query_duration(nint element, int format, out long duration);
    [DllImport(Gst)] private static extern int gst_element_seek_simple(nint element, int format, int flags, long position);
    [DllImport(Gst)] private static extern nint gst_element_get_bus(nint element);
    [DllImport(Gst)] private static extern nint gst_bus_pop_filtered(nint bus, int types);
    [DllImport(Gst)] private static extern void gst_message_parse_error(nint message, out nint error, out nint debug);
    [DllImport(Gst)] private static extern void gst_mini_object_unref(nint message);
    [DllImport(Gst)] private static extern void gst_object_unref(nint element);
    [DllImport(GObject)] private static extern nint g_value_init(ref GValue value, nuint type);
    [DllImport(GObject)] private static extern void g_value_set_string(ref GValue value, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(GObject)] private static extern void g_value_set_double(ref GValue value, double number);
    [DllImport(GObject)] private static extern void g_value_set_boolean(ref GValue value, int flag);
    [DllImport(GObject)] private static extern void g_value_unset(ref GValue value);
    [DllImport(GObject)] private static extern void g_object_set_property(nint target, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref GValue value);
    [DllImport(GLib)] private static extern void g_error_free(nint error);
    [DllImport(GLib)] private static extern void g_free(nint memory);

    private static bool _initialized;
    private readonly nint _playbin;
    private readonly nint _bus;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _open;
    private bool _playing;
    private string? _tempFile;
    private double _volume = 1;
    private bool _muted;

    public event Action? Ended;
    public event Action<string>? Failed;
    public event Action? StateChanged;

    internal static bool Available()
    {
        try
        {
            if (!Initialize()) return false;
            nint factory = gst_element_factory_find("playbin");
            if (factory == 0) return false;
            gst_object_unref(factory);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return false;
        }
    }

    private static bool Initialize()
    {
        if (_initialized) return true;
        if (gst_init_check(0, 0, out nint error) == 0)
        {
            if (error != 0) g_error_free(error);
            return false;
        }
        _initialized = true;
        return true;
    }

    public GStreamerLocalPlayer()
    {
        if (!Initialize()) throw new InvalidOperationException("GStreamer couldn't start.");
        _playbin = gst_element_factory_make("playbin", 0);
        if (_playbin == 0) throw new InvalidOperationException("GStreamer's playbin isn't installed.");
        _bus = gst_element_get_bus(_playbin);
        _poll.Tick += (_, _) => Poll();
    }

    public bool Playing => _open && _playing;

    public TimeSpan Position =>
        _open && gst_element_query_position(_playbin, FormatTime, out long position) != 0 && position >= 0 ? TimeSpan.FromTicks(position / NanosPerTick) : TimeSpan.Zero;

    public TimeSpan Duration =>
        _open && gst_element_query_duration(_playbin, FormatTime, out long duration) != 0 && duration > 0 ? TimeSpan.FromTicks(duration / NanosPerTick) : TimeSpan.Zero;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            var gvalue = new GValue();
            g_value_init(ref gvalue, TypeDouble);
            g_value_set_double(ref gvalue, _volume);
            g_object_set_property(_playbin, "volume", ref gvalue);
            g_value_unset(ref gvalue);
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            var gvalue = new GValue();
            g_value_init(ref gvalue, TypeBoolean);
            g_value_set_boolean(ref gvalue, value ? 1 : 0);
            g_object_set_property(_playbin, "mute", ref gvalue);
            g_value_unset(ref gvalue);
        }
    }

    public void Open(Uri url) => OpenUri(url.AbsoluteUri, null);

    public void Open(Stream stream, string contentType)
    {
        string path = LocalTempFile.Write(stream, contentType);
        OpenUri(new Uri(path).AbsoluteUri, path);
    }

    private void OpenUri(string uri, string? tempFile)
    {
        gst_element_set_state(_playbin, StateNull);
        DrainBus();
        LocalTempFile.Delete(_tempFile);
        _tempFile = tempFile;
        var gvalue = new GValue();
        g_value_init(ref gvalue, TypeString);
        g_value_set_string(ref gvalue, uri);
        g_object_set_property(_playbin, "uri", ref gvalue);
        g_value_unset(ref gvalue);
        _open = true;
        _playing = false;
        gst_element_set_state(_playbin, StatePaused);
        _poll.Start();
    }

    public void Play()
    {
        if (!_open) return;
        gst_element_set_state(_playbin, StatePlaying);
        _playing = true;
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        if (!_open) return;
        gst_element_set_state(_playbin, StatePaused);
        _playing = false;
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        gst_element_set_state(_playbin, StateNull);
        DrainBus();
        _open = _playing = false;
        _poll.Stop();
        LocalTempFile.Delete(_tempFile);
        _tempFile = null;
        StateChanged?.Invoke();
    }

    public void Seek(TimeSpan position)
    {
        if (_open) gst_element_seek_simple(_playbin, FormatTime, SeekFlush | SeekKeyUnit, position.Ticks * NanosPerTick);
    }

    private void DrainBus()
    {
        nint message;
        while ((message = gst_bus_pop_filtered(_bus, MessageEos | MessageError)) != 0) gst_mini_object_unref(message);
    }

    private void Poll()
    {
        if (!_open) return;
        nint error = gst_bus_pop_filtered(_bus, MessageError);
        if (error != 0)
        {
            string text = "GStreamer couldn't play it";
            gst_message_parse_error(error, out nint gerror, out nint debug);
            if (gerror != 0)
            {
                if (Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(gerror, 8)) is { Length: > 0 } message) text = message;
                g_error_free(gerror);
            }
            if (debug != 0) g_free(debug);
            gst_mini_object_unref(error);
            gst_element_set_state(_playbin, StateNull);
            _open = _playing = false;
            _poll.Stop();
            Failed?.Invoke(text);
            return;
        }
        nint end = gst_bus_pop_filtered(_bus, MessageEos);
        if (end == 0) return;
        gst_mini_object_unref(end);
        _playing = false;
        Ended?.Invoke();
    }

    public void Dispose()
    {
        _poll.Stop();
        gst_element_set_state(_playbin, StateNull);
        if (_bus != 0) gst_object_unref(_bus);
        gst_object_unref(_playbin);
        LocalTempFile.Delete(_tempFile);
    }
}
