using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace WiimControl;

sealed class FireVisual : Control
{
    internal const int SampleCount = SpectrumEngine.SampleCount;
    private const int Columns = 64;
    private const int MaxSparks = 260;
    private const float ProcessorScale = 4;
    private const string Shader = """
        uniform float2 u_res;
        uniform float u_time;
        uniform float3 u_c0;
        uniform float3 u_c1;
        uniform float3 u_c2;
        uniform float3 u_c3;
        uniform float u_pulse;
        uniform float u_loudness;
        uniform shader u_levels;

        float easeBetween(float low, float high, float x) {
            float t = clamp((x - low) / (high - low), 0.0, 1.0);
            return t * t * (3.0 - 2.0 * t);
        }

        float hash(float2 p) {
            p = fract(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return fract(p.x * p.y);
        }

        float noise(float2 p) {
            float2 i = floor(p);
            float2 f = fract(p);
            float2 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash(i), hash(i + float2(1.0, 0.0)), u.x),
                       mix(hash(i + float2(0.0, 1.0)), hash(i + float2(1.0, 1.0)), u.x), u.y);
        }

        float fbm(float2 p) {
            float value = 0.0;
            float amp = 0.5;
            for (int k = 0; k < 5; k++) {
                value += amp * noise(p);
                p = p * 2.03 + float2(1.7, 9.2);
                amp *= 0.5;
            }
            return value;
        }

        float levelAt(float x) {
            float pos = clamp(x, 0.0, 1.0) * 63.0;
            float i = floor(pos);
            float a = sample(u_levels, float2(i + 0.5, 0.5)).r;
            float b = sample(u_levels, float2(min(i + 1.0, 63.0) + 0.5, 0.5)).r;
            return mix(a, b, pos - i);
        }

        half4 main(float2 coord) {
            float2 uv = float2(coord.x / u_res.x, 1.0 - coord.y / u_res.y);
            float aspect = u_res.x / max(u_res.y, 1.0);
            float level = levelAt(uv.x);
            float height = 0.08 + 0.85 * level + 0.08 * u_pulse;
            float t = u_time;
            float2 p = float2(uv.x * aspect * 4.2, uv.y * 1.6);
            float2 q = float2(fbm(p + float2(0.0, -t * 1.6)), fbm(p * 1.3 + float2(5.2, -t * 1.9)));
            float n = fbm(p * 1.25 + q * 2.1 + float2(0.0, -t * 2.8));
            float y = uv.y / max(height, 0.04);
            float heat = clamp(1.0 - y + (n - 0.48) * 1.5 * (0.3 + y), 0.0, 1.0);
            float3 colour;
            if (heat < 0.33) {
                colour = mix(u_c0 * 0.6, u_c1, heat / 0.33);
            } else if (heat < 0.66) {
                colour = mix(u_c1, u_c2, (heat - 0.33) / 0.33);
            } else {
                colour = mix(u_c2, u_c3, (heat - 0.66) / 0.34);
            }
            float body = easeBetween(0.04, 0.32, heat);
            float glow = (1.0 - easeBetween(0.0, 0.18, uv.y)) * (0.18 + 0.3 * u_loudness) * (1.0 - body);
            float alpha = clamp(body + glow, 0.0, 1.0);
            colour = (colour * body + u_c1 * glow) / max(alpha, 0.001);
            return half4(colour * alpha, alpha);
        }
        """;

    private static readonly Lazy<SKRuntimeEffect?> FireEffect = new(() =>
    {
        try
        {
            return SKRuntimeEffect.Create(Shader, out _);
        }
        catch (Exception) { return null; }
    });

    private readonly SpectrumEngine _engine = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly Random _random = new();
    private readonly List<Spark> _sparks = [];
    private readonly float[] _columns = new float[Columns];
    private bool _showBackground = true;

    private record struct Spark(double X, double Y, double Vx, double Vy, double Life);

    public bool ShowBackground
    {
        get => _showBackground;
        set { _showBackground = value; InvalidateVisual(); }
    }

    internal void Feed(float[] samples, int sampleRate, double seconds)
    {
        _engine.Feed(samples, sampleRate, seconds);
        UpdateColumns();
        UpdateSparks(Math.Clamp(seconds, 0.001, 0.05));
        InvalidateVisual();
    }

    internal void Reset()
    {
        _engine.Reset();
        _sparks.Clear();
        Array.Clear(_columns);
        InvalidateVisual();
    }

    private void UpdateColumns()
    {
        var raw = new float[Columns + 4];
        for (int i = 0; i < Columns; i++) raw[i + 2] = _engine.LevelAt((i + 0.5) / Columns);
        raw[0] = raw[1] = raw[2];
        raw[^1] = raw[^2] = raw[^3];
        for (int i = 0; i < Columns; i++)
            _columns[i] = (raw[i] + 2 * raw[i + 1] + 3 * raw[i + 2] + 2 * raw[i + 3] + raw[i + 4]) / 9f;
    }

    private void UpdateSparks(double dt)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        double baseline = h * 0.9, spread = h * 0.35;
        int spawn = (int)(_engine.BassLevel() * 5 + _engine.Pulse * 12 + _random.NextDouble() * 1.5);
        if (spawn > 0)
        {
            double total = _columns.Sum(c => c * c + 1e-3);
            for (int s = 0; s < spawn; s++)
            {
                double pick = _random.NextDouble() * total;
                int index = 0;
                for (; index < Columns - 1; index++)
                {
                    pick -= _columns[index] * _columns[index] + 1e-3;
                    if (pick <= 0) break;
                }
                double level = _columns[index];
                _sparks.Add(new Spark((index + 0.5) / Columns * w + Uniform(-4, 4), baseline - Uniform(0, spread) * (0.3 + level),
                    Uniform(-15, 15), -Uniform(40, 160) * (0.6 + level), 1));
            }
            if (_sparks.Count > MaxSparks) _sparks.RemoveRange(0, _sparks.Count - MaxSparks);
        }
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var s = _sparks[i];
            s = s with { X = s.X + s.Vx * dt + Uniform(-0.6, 0.6), Y = s.Y + s.Vy * dt, Vy = s.Vy + 25 * dt, Life = s.Life - dt * 0.7 };
            if (s.Life <= 0 || s.Y < -10 || s.Y > h + 10) _sparks.RemoveAt(i);
            else _sparks[i] = s;
        }
    }

    private double Uniform(double low, double high) => low + (high - low) * _random.NextDouble();

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new FireFrame(new Rect(0, 0, w, h), (float[])_columns.Clone(), (float)(_clock.Elapsed.TotalSeconds % 600),
            (float)_engine.Pulse, _engine.Loudness(), _sparks.Select(s => (s.X, s.Y, s.Life)).ToArray(), _showBackground);
        context.Custom(frame);
    }

    private sealed class FireFrame(Rect bounds, float[] columns, float time, float pulse, float loudness, (double X, double Y, double Life)[] sparks, bool background)
        : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        private static float[] Vector(Color c) => [c.R / 255f, c.G / 255f, c.B / 255f];

        private static SKColor Sk(Color c) => new(c.R, c.G, c.B, c.A);

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var area = new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height);
            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(area, 6, 6), antialias: true);
            if (background)
            {
                using var fill = new SKPaint { Color = Sk(VisualTheme.Background) };
                canvas.DrawRect(area, fill);
            }
            if (lease.GrContext != null && FireEffect.Value is { } effect) DrawWithShader(canvas, area, effect);
            else DrawWithProcessor(canvas, area);
            DrawSparks(canvas);
            canvas.Restore();
        }

        private void DrawWithShader(SKCanvas canvas, SKRect area, SKRuntimeEffect effect)
        {
            var pixels = new byte[columns.Length * 4];
            for (int i = 0; i < columns.Length; i++)
            {
                pixels[i * 4] = (byte)Math.Clamp(columns[i] * 255, 0, 255);
                pixels[i * 4 + 3] = 255;
            }
            using var levels = SKImage.FromPixelCopy(new SKImageInfo(columns.Length, 1, SKColorType.Rgba8888, SKAlphaType.Premul), pixels);
            using var levelShader = levels.ToShader();
            var uniforms = new SKRuntimeEffectUniforms(effect)
            {
                ["u_res"] = new[] { area.Width, area.Height },
                ["u_time"] = time,
                ["u_c0"] = Vector(VisualTheme.GradientAt(0)),
                ["u_c1"] = Vector(VisualTheme.GradientAt(0.4)),
                ["u_c2"] = Vector(VisualTheme.GradientAt(0.8)),
                ["u_c3"] = Vector(VisualTheme.Hot),
                ["u_pulse"] = pulse,
                ["u_loudness"] = loudness
            };
            var children = new SKRuntimeEffectChildren(effect) { ["u_levels"] = levelShader };
            using var shader = effect.ToShader(false, uniforms, children);
            using var paint = new SKPaint { Shader = shader };
            canvas.DrawRect(area, paint);
        }

        private void DrawWithProcessor(SKCanvas canvas, SKRect area)
        {
            int width = Math.Max(1, (int)(area.Width / ProcessorScale)), height = Math.Max(1, (int)(area.Height / ProcessorScale));
            var pixels = FireProcessor.Render(width, height, area.Width / area.Height, columns, time, pulse, loudness);
            using var image = SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), pixels);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium };
            canvas.DrawImage(image, area, paint);
        }

        private void DrawSparks(SKCanvas canvas)
        {
            var hot = VisualTheme.Hot;
            foreach (var (low, high, size) in new[] { (0.0, 0.35, 1.6f), (0.35, 0.7, 2.4f), (0.7, 1.01, 3.0f) })
            {
                double middle = (low + high) / 2;
                var colour = VisualTheme.WithAlpha(VisualTheme.Mix(VisualTheme.GradientAt(0.3), hot, middle), Math.Min(1, 0.3 + middle) * 255);
                using var dot = new SKPaint { Color = Sk(colour), IsAntialias = true };
                foreach (var spark in sparks)
                    if (spark.Life >= low && spark.Life < high) canvas.DrawCircle((float)spark.X, (float)spark.Y, size / 2, dot);
            }
        }
    }

    private static class FireProcessor
    {
        private static float Fract(float v) => v - MathF.Floor(v);

        private static float Hash(float x, float y)
        {
            x = Fract(x * 123.34f);
            y = Fract(y * 456.21f);
            float d = x * (x + 45.32f) + y * (y + 45.32f);
            x += d;
            y += d;
            return Fract(x * y);
        }

        private static float Noise(float x, float y)
        {
            float ix = MathF.Floor(x), iy = MathF.Floor(y);
            float fx = x - ix, fy = y - iy;
            float ux = fx * fx * (3 - 2 * fx), uy = fy * fy * (3 - 2 * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            float top = a + (b - a) * ux, bottom = c + (d - c) * ux;
            return top + (bottom - top) * uy;
        }

        private static float Fbm(float x, float y)
        {
            float value = 0, amp = 0.5f;
            for (int k = 0; k < 5; k++)
            {
                value += amp * Noise(x, y);
                x = x * 2.03f + 1.7f;
                y = y * 2.03f + 9.2f;
                amp *= 0.5f;
            }
            return value;
        }

        private static float Ease(float low, float high, float x)
        {
            float t = Math.Clamp((x - low) / (high - low), 0, 1);
            return t * t * (3 - 2 * t);
        }

        public static byte[] Render(int width, int height, float aspect, float[] columns, float time, float pulse, float loudness)
        {
            var c0 = Colour(VisualTheme.GradientAt(0));
            var c1 = Colour(VisualTheme.GradientAt(0.4));
            var c2 = Colour(VisualTheme.GradientAt(0.8));
            var c3 = Colour(VisualTheme.Hot);
            var pixels = new byte[width * height * 4];
            Parallel.For(0, height, row =>
            {
                float v = 1f - (row + 0.5f) / height;
                for (int col = 0; col < width; col++)
                {
                    float u = (col + 0.5f) / width;
                    float pos = Math.Clamp(u, 0, 1) * (columns.Length - 1);
                    int i = (int)MathF.Floor(pos);
                    int j = Math.Min(i + 1, columns.Length - 1);
                    float level = columns[i] + (columns[j] - columns[i]) * (pos - i);
                    float height01 = 0.08f + 0.85f * level + 0.08f * pulse;
                    float px = u * aspect * 4.2f, py = v * 1.6f;
                    float qx = Fbm(px, py - time * 1.6f);
                    float qy = Fbm(px * 1.3f + 5.2f, py * 1.3f - time * 1.9f);
                    float n = Fbm(px * 1.25f + qx * 2.1f, py * 1.25f + qy * 2.1f - time * 2.8f);
                    float y = v / MathF.Max(height01, 0.04f);
                    float heat = Math.Clamp(1 - y + (n - 0.48f) * 1.5f * (0.3f + y), 0, 1);
                    (float R, float G, float B) colour = heat < 0.33f ? Mix((c0.R * 0.6f, c0.G * 0.6f, c0.B * 0.6f), c1, heat / 0.33f)
                        : heat < 0.66f ? Mix(c1, c2, (heat - 0.33f) / 0.33f)
                        : Mix(c2, c3, (heat - 0.66f) / 0.34f);
                    float body = Ease(0.04f, 0.32f, heat);
                    float glow = (1 - Ease(0, 0.18f, v)) * (0.18f + 0.3f * loudness) * (1 - body);
                    float alpha = Math.Clamp(body + glow, 0, 1);
                    int at = (row * width + col) * 4;
                    pixels[at] = (byte)Math.Clamp((colour.R * body + c1.R * glow) * 255, 0, 255);
                    pixels[at + 1] = (byte)Math.Clamp((colour.G * body + c1.G * glow) * 255, 0, 255);
                    pixels[at + 2] = (byte)Math.Clamp((colour.B * body + c1.B * glow) * 255, 0, 255);
                    pixels[at + 3] = (byte)(alpha * 255);
                }
            });
            return pixels;
        }

        private static (float R, float G, float B) Colour(Color c) => (c.R / 255f, c.G / 255f, c.B / 255f);

        private static (float R, float G, float B) Mix((float R, float G, float B) a, (float R, float G, float B) b, float t) =>
            (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
    }
}
