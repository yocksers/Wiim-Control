using Avalonia.Media;

namespace WiimControl;

static class VisualTheme
{
    internal static readonly Color Background = Color.FromRgb(10, 6, 16);
    private static readonly Color[] Gradient = [Color.FromRgb(255, 0, 120), Color.FromRgb(255, 210, 0), Color.FromRgb(0, 255, 230)];

    internal static Color GradientStop(int index) => Gradient[index];

    internal static Color GradientAt(double t)
    {
        t = Math.Clamp(t, 0, 1) * (Gradient.Length - 1);
        int i = Math.Min((int)t, Gradient.Length - 2);
        return Mix(Gradient[i], Gradient[i + 1], t - i);
    }

    internal static Color Hot => Mix(GradientAt(1), Colors.White, 0.45);

    internal static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    internal static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(alpha, 0, 255), c.R, c.G, c.B);
}

sealed class SpectrumEngine
{
    internal const int SampleCount = LongFft;
    internal const int BandCount = 32;
    internal const double FrameSeconds = 0.016;
    private const double LowestHz = 30;
    private const double HighestHz = 16000;
    private const int ShortFft = 2048;
    private const int LongFft = 8192;
    private const int NarrowBandBins = 4;
    private const double TiltReferenceHz = 30;
    private const double TrebleBoostDbPerOctave = 3;
    private const double Sensitivity = 0.25;
    private const double MaxTiltDb = 24;
    private const double RangeDb = 60;
    private const double Attack = 0.6;
    private const double Decay = 0.15;
    private const int PeakHoldFrames = 31;
    private const double PeakFall = 9 * 0.0001;
    private const double PulseDecay = 0.86;

    private readonly float[] _bands = new float[BandCount];
    private readonly Fft _short = new(ShortFft);
    private readonly Fft _long = new(LongFft);
    private readonly BeatDetector _beat = new();
    private readonly float[] _peakVel = new float[BandCount];
    private readonly int[] _peakHold = new int[BandCount];
    private double _pending;
    private int _sampleRate;
    private double[] _edges = [];
    private double[] _centers = [];
    private bool[] _narrow = [];

    public float[] Levels { get; } = new float[BandCount];
    public float[] Peaks { get; } = new float[BandCount];
    public double Pulse { get; private set; }

    public bool Feed(float[] samples, int sampleRate, double seconds)
    {
        if (sampleRate != _sampleRate) Prepare(sampleRate);
        ComputeBands(samples);
        if (_beat.Process(samples, sampleRate)) Pulse = 1;
        _pending += Math.Clamp(seconds, 0, 0.25);
        bool changed = false;
        while (_pending >= FrameSeconds)
        {
            _pending -= FrameSeconds;
            Step();
            changed = true;
        }
        return changed;
    }

    public void Reset()
    {
        Array.Clear(Levels);
        Array.Clear(Peaks);
        Array.Clear(_peakVel);
        Array.Clear(_peakHold);
        _pending = 0;
        Pulse = 0;
    }

    public float LevelAt(double position)
    {
        double at = Math.Clamp(position, 0, 1) * BandCount - 0.5;
        if (at <= 0) return Levels[0];
        if (at >= BandCount - 1) return Levels[^1];
        int i = (int)at;
        return (float)(Levels[i] + (Levels[i + 1] - Levels[i]) * (at - i));
    }

    public float BassLevel()
    {
        int count = Math.Max(1, BandCount / 8);
        float sum = 0;
        for (int i = 0; i < count; i++) sum += Levels[i];
        return sum / count;
    }

    public float Loudness() => Levels.Average();

    private void Prepare(int sampleRate)
    {
        _sampleRate = sampleRate;
        double high = Math.Min(HighestHz, sampleRate / 2.0 - 1);
        double low = Math.Log10(LowestHz), top = Math.Log10(Math.Max(high, LowestHz + 1));
        _edges = new double[BandCount + 1];
        for (int i = 0; i <= BandCount; i++) _edges[i] = Math.Pow(10, low + (top - low) * i / BandCount);
        _centers = new double[BandCount];
        _narrow = new bool[BandCount];
        for (int i = 0; i < BandCount; i++)
        {
            _centers[i] = Math.Sqrt(_edges[i] * _edges[i + 1]);
            _narrow[i] = _edges[i + 1] - _edges[i] < NarrowBandBins * (double)sampleRate / ShortFft;
        }
    }

    private void ComputeBands(float[] samples)
    {
        var shortSpectrum = _short.Magnitudes(samples, samples.Length - ShortFft);
        var longSpectrum = _narrow.Any(n => n) ? _long.Magnitudes(samples, samples.Length - LongFft) : shortSpectrum;
        for (int i = 0; i < BandCount; i++)
        {
            bool narrow = _narrow[i];
            var spectrum = narrow ? longSpectrum : shortSpectrum;
            int size = narrow ? LongFft : ShortFft;
            double step = (double)_sampleRate / size;
            double position = _centers[i] / step;
            int below = Math.Clamp((int)Math.Floor(position), 0, spectrum.Length - 1);
            int above = Math.Min(below + 1, spectrum.Length - 1);
            double value = spectrum[below] + (spectrum[above] - spectrum[below]) * (position - below);
            int first = (int)Math.Ceiling(_edges[i] / step);
            double loudest = -1;
            for (int k = Math.Max(0, first); k < spectrum.Length && k * step < _edges[i + 1]; k++) loudest = Math.Max(loudest, spectrum[k]);
            if (loudest >= 0) value = loudest;
            double tiltDb = Math.Clamp(TrebleBoostDbPerOctave * Math.Log2(_centers[i] / TiltReferenceHz), 0, MaxTiltDb);
            value *= Math.Pow(10, tiltDb / 20);
            double db = 20 * Math.Log10(value * Sensitivity + 1e-6);
            _bands[i] = (float)Math.Clamp((db + RangeDb) / RangeDb, 0, 1);
        }
    }

    private void Step()
    {
        for (int i = 0; i < BandCount; i++)
        {
            float band = _bands[i];
            Levels[i] += (band - Levels[i]) * (float)(band > Levels[i] ? Attack : Decay);
            float level = Levels[i];
            if (level >= Peaks[i])
            {
                Peaks[i] = level;
                _peakVel[i] = 0;
                _peakHold[i] = PeakHoldFrames;
            }
            else if (_peakHold[i] > 0) _peakHold[i]--;
            else
            {
                _peakVel[i] += (float)PeakFall;
                Peaks[i] -= _peakVel[i];
            }
            if (Peaks[i] < level)
            {
                Peaks[i] = level;
                _peakVel[i] = 0;
            }
        }
        Pulse *= PulseDecay;
    }

    private sealed class BeatDetector
    {
        private const double KickLowHz = 40;
        private const double KickHighHz = 150;
        private const int Window = 1024;
        private const int History = 60;
        private const double MinInterval = 0.28;
        private const double SilenceDb = -48;
        private const double MinJumpDb = 2.5;
        private const double RiseDb = 1;

        private readonly Fft _fft = new(Window);
        private readonly Queue<double> _history = new();
        private double? _previous;
        private double _lastBeat = -10;
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        public bool Process(float[] samples, int sampleRate)
        {
            if (sampleRate <= 0) return false;
            var spectrum = _fft.Magnitudes(samples, samples.Length - Window);
            double step = (double)sampleRate / Window, power = 0;
            for (int k = 0; k < spectrum.Length; k++)
            {
                double f = k * step;
                if (f >= KickLowHz && f <= KickHighHz) power += spectrum[k] * spectrum[k];
            }
            double energy = 10 * Math.Log10(power + 1e-12);
            double now = _clock.Elapsed.TotalSeconds;
            bool beat = false;
            if (_history.Count >= 20 && _previous is double previous)
            {
                double mean = _history.Average();
                double spread = Math.Sqrt(_history.Average(v => (v - mean) * (v - mean)));
                double needed = Math.Max(MinJumpDb, 1.5 * spread);
                if (energy > mean + needed && energy - previous > RiseDb && energy > SilenceDb && now - _lastBeat > MinInterval)
                {
                    beat = true;
                    _lastBeat = now;
                }
            }
            _history.Enqueue(energy);
            if (_history.Count > History) _history.Dequeue();
            _previous = energy;
            return beat;
        }
    }

    internal sealed class Fft
    {
        private readonly int _size;
        private readonly float[] _window;
        private readonly double _scale;
        private readonly double[] _re;
        private readonly double[] _im;
        private readonly double[] _cos;
        private readonly double[] _sin;
        private readonly int[] _reverse;
        private readonly double[] _magnitudes;

        public Fft(int size)
        {
            _size = size;
            _window = new float[size];
            double sum = 0;
            for (int i = 0; i < size; i++)
            {
                _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (size - 1)));
                sum += _window[i];
            }
            _scale = 2.0 / sum;
            _re = new double[size];
            _im = new double[size];
            _cos = new double[size / 2];
            _sin = new double[size / 2];
            for (int i = 0; i < size / 2; i++)
            {
                _cos[i] = Math.Cos(-2 * Math.PI * i / size);
                _sin[i] = Math.Sin(-2 * Math.PI * i / size);
            }
            int bits = (int)Math.Log2(size);
            _reverse = new int[size];
            for (int i = 0; i < size; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
                _reverse[i] = r;
            }
            _magnitudes = new double[size / 2 + 1];
        }

        public double[] Magnitudes(float[] samples, int start)
        {
            for (int i = 0; i < _size; i++)
            {
                int at = start + i;
                _re[_reverse[i]] = at >= 0 && at < samples.Length ? samples[at] * _window[i] : 0;
                _im[_reverse[i]] = 0;
            }
            for (int length = 2; length <= _size; length <<= 1)
            {
                int half = length / 2, stride = _size / length;
                for (int block = 0; block < _size; block += length)
                    for (int k = 0; k < half; k++)
                    {
                        double c = _cos[k * stride], s = _sin[k * stride];
                        int a = block + k, b = a + half;
                        double tr = _re[b] * c - _im[b] * s, ti = _re[b] * s + _im[b] * c;
                        _re[b] = _re[a] - tr;
                        _im[b] = _im[a] - ti;
                        _re[a] += tr;
                        _im[a] += ti;
                    }
            }
            for (int k = 0; k <= _size / 2; k++) _magnitudes[k] = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) * _scale;
            return _magnitudes;
        }
    }
}
