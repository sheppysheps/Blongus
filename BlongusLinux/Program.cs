using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

namespace BlongusLinux;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        bool forceCli = args.Any(a =>
            a.Equals("--cli", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-c", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("cli", StringComparison.OrdinalIgnoreCase));

        bool forceGui = args.Any(a =>
            a.Equals("--gui", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-g", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("gui", StringComparison.OrdinalIgnoreCase));

        bool showHelp = args.Any(a =>
            a.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-h", StringComparison.OrdinalIgnoreCase));

        if (showHelp)
        {
            Console.WriteLine("blongus - Generates a nonsense word from three dictionary parts.");
            Console.WriteLine("Usage: blongus [--cli | --gui | --help]");
            Console.WriteLine("  --cli, -c   Print the generated word to stdout and exit.");
            Console.WriteLine("  --gui, -g   Show the 5-second confetti celebration overlay.");
            return 0;
        }

        bool hasDisplay =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

        if (forceCli || (!forceGui && (Console.IsOutputRedirected || !hasDisplay)))
        {
            Console.WriteLine(BlongusGenerator.Generate());
            Console.Out.Flush();
            return 0;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<BlongusApp>()
            .UsePlatformDetect()
            .UseSkia();
}

internal static class BlongusGenerator
{
    private static readonly Assembly CurrentAssembly = Assembly.GetExecutingAssembly();

    public static string Generate()
    {
        // 1 in 100 chance to pull directly from special.txt instead of first/second/third
        if (Random.Shared.Next(100) == 0)
        {
            string[] special = ReadLines("Blongus.special.txt");
            return PickRandom(special);
        }

        string[] first = ReadLines("Blongus.first.txt");
        string[] second = ReadLines("Blongus.second.txt");

        string word = PickRandom(first) + PickRandom(second);

        // 1 in 8 chance that the third part is omitted (matches blongus.py)
        if (Random.Shared.Next(8) != 0)
        {
            string[] third = ReadLines("Blongus.third.txt");
            word += PickRandom(third);
        }

        return word;
    }

    private static string[] ReadLines(string resourceName)
    {
        using Stream stream = CurrentAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource: {resourceName}");
        using var reader = new StreamReader(stream);

        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                lines.Add(trimmed);
            }
        }

        if (lines.Count == 0)
        {
            throw new InvalidOperationException($"Resource {resourceName} contains no entries.");
        }

        return lines.ToArray();
    }

    private static string PickRandom(string[] items) => items[Random.Shared.Next(items.Length)];
}

internal sealed class BlongusApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new CelebrationWindow(BlongusGenerator.Generate());
        }

        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class CelebrationWindow : Window
{
    private readonly CelebrationCanvas _canvas;
    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _closeTimer;
    private readonly LinuxAudioPlayer _audioPlayer = new();

    public CelebrationWindow(string word)
    {
        Title = "Blongus";
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        WindowState = WindowState.FullScreen;

        using Stream? iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Blongus.scrimmy.png");
        if (iconStream is not null)
        {
            Icon = new WindowIcon(iconStream);
        }

        _canvas = new CelebrationCanvas(word);
        Content = _canvas;

        _frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _frameTimer.Tick += (_, _) =>
        {
            _canvas.StepAnimation();
            _canvas.InvalidateVisual();
        };

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _closeTimer.Tick += (_, _) => Close();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        TryEnableX11ClickThrough();
        _audioPlayer.PlayEmbeddedWav("Blongus.children_cheering.wav");
        _frameTimer.Start();
        _closeTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _frameTimer.Stop();
        _closeTimer.Stop();
        _audioPlayer.Dispose();
        base.OnClosed(e);
    }

    private void TryEnableX11ClickThrough()
    {
        try
        {
            IPlatformHandle? handle = TryGetPlatformHandle();
            if (handle is null || handle.HandleDescriptor != "XID")
            {
                return;
            }

            IntPtr display = X11Native.XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
            {
                return;
            }

            try
            {
                // ShapeInput = 2, ShapeSet = 0, Unsorted = 0 with 0 rectangles => full click-through
                X11Native.XShapeCombineRectangles(display, handle.Handle, 2, 0, 0, IntPtr.Zero, 0, 0, 0);
                X11Native.XFlush(display);
            }
            finally
            {
                X11Native.XCloseDisplay(display);
            }
        }
        catch
        {
            // Ignore when running on pure Wayland or without libXext
        }
    }
}

internal static class X11Native
{
    [DllImport("libX11.so.6")]
    public static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    public static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    public static extern int XFlush(IntPtr display);

    [DllImport("libXext.so.6")]
    public static extern void XShapeCombineRectangles(
        IntPtr display,
        IntPtr dest,
        int destKind,
        int xOff,
        int yOff,
        IntPtr rectangles,
        int nRects,
        int op,
        int ordering);
}

internal sealed class LinuxAudioPlayer : IDisposable
{
    private Process? _playerProcess;
    private string? _tempWavPath;

    public void PlayEmbeddedWav(string resourceName)
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return;
            }

            _tempWavPath = Path.Combine(Path.GetTempPath(), $"blongus_{Guid.NewGuid():N}.wav");
            using (FileStream fs = File.Create(_tempWavPath))
            {
                stream.CopyTo(fs);
            }

            (string cmd, string args)[] candidates =
            [
                ("pw-play", $"\"{_tempWavPath}\""),
                ("paplay", $"\"{_tempWavPath}\""),
                ("aplay", $"-q \"{_tempWavPath}\""),
                ("ffplay", $"-nodisp -autoexit -loglevel quiet \"{_tempWavPath}\"")
            ];

            foreach ((string cmd, string args) in candidates)
            {
                if (TryStartPlayer(cmd, args))
                {
                    break;
                }
            }
        }
        catch
        {
            // Non-fatal if no audio backend is present
        }
    }

    private bool TryStartPlayer(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            _playerProcess = Process.Start(psi);
            return _playerProcess is not null;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_playerProcess is { HasExited: false })
            {
                _playerProcess.Kill(entireProcessTree: true);
            }
            _playerProcess?.Dispose();
        }
        catch
        {
            // Ignore cleanup errors
        }

        try
        {
            if (_tempWavPath is not null && File.Exists(_tempWavPath))
            {
                File.Delete(_tempWavPath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

internal sealed class CelebrationCanvas : Control
{
    private static readonly SKColor[] NeonRainbow =
    [
        new SKColor(255, 20, 147),  // Neon Pink
        new SKColor(255, 59, 0),    // Neon Red-Orange
        new SKColor(255, 255, 0),   // Neon Yellow
        new SKColor(57, 255, 20),   // Neon Lime
        new SKColor(0, 255, 255),   // Neon Cyan
        new SKColor(0, 128, 255),   // Neon Electric Blue
        new SKColor(191, 0, 255),   // Neon Violet
        new SKColor(255, 20, 147)   // Wrap
    ];

    private readonly string _word;
    private readonly List<ConfettiPiece> _confetti = [];
    private readonly DateTime _startTime = DateTime.UtcNow;
    private readonly SKTypeface _typeface;
    private bool _initialized;

    public CelebrationCanvas(string word)
    {
        _word = word;
        bool needsShiftJisFont = word.Any(c => c > 0x7E);
        _typeface = needsShiftJisFont
            ? LoadEmbeddedTypeface("Blongus.MPLUS1p-Bold.ttf", "MS PGothic")
            : LoadEmbeddedTypeface("Blongus.ComicNeue-Bold.ttf", "Comic Sans MS");
    }

    private static SKTypeface LoadEmbeddedTypeface(string resourceName, string fallbackFamily)
    {
        using Stream? fontStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (fontStream is not null)
        {
            return SKTypeface.FromStream(fontStream);
        }

        return SKTypeface.FromFamilyName(fallbackFamily, SKFontStyle.Bold)
            ?? SKTypeface.Default;
    }

    public void StepAnimation()
    {
        float w = (float)Bounds.Width;
        float h = (float)Bounds.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        if (!_initialized)
        {
            InitConfetti(w, h);
            _initialized = true;
        }

        foreach (ConfettiPiece p in _confetti)
        {
            p.SwayPhase += 0.08f;
            p.X += p.Vx + MathF.Sin(p.SwayPhase) * 1.8f;
            p.Y += p.Vy;
            p.Angle += p.Spin;

            if (p.Y > h + 30f)
            {
                p.Y = -20f;
                p.X = Random.Shared.NextSingle() * w;
            }
        }
    }

    private void InitConfetti(float width, float height)
    {
        for (int i = 0; i < 220; i++)
        {
            _confetti.Add(new ConfettiPiece
            {
                X = Random.Shared.NextSingle() * width,
                Y = Random.Shared.NextSingle() * height - height * 0.5f,
                Vx = (Random.Shared.NextSingle() - 0.5f) * 3.5f,
                Vy = 4.0f + Random.Shared.NextSingle() * 7.5f,
                Width = 10f + Random.Shared.NextSingle() * 14f,
                Height = 6f + Random.Shared.NextSingle() * 10f,
                Angle = Random.Shared.NextSingle() * 360f,
                Spin = (Random.Shared.NextSingle() - 0.5f) * 14f,
                SwayPhase = Random.Shared.NextSingle() * MathF.Tau,
                Color = NeonRainbow[Random.Shared.Next(NeonRainbow.Length - 1)]
            });
        }
    }

    public override void Render(DrawingContext context)
    {
        if (!_initialized && Bounds.Width > 0 && Bounds.Height > 0)
        {
            InitConfetti((float)Bounds.Width, (float)Bounds.Height);
            _initialized = true;
        }

        float elapsed = (float)(DateTime.UtcNow - _startTime).TotalSeconds;
        context.Custom(new CelebrationDrawOp(
            new Rect(0, 0, Bounds.Width, Bounds.Height),
            _word,
            _typeface,
            _confetti,
            elapsed));
    }

    private sealed class CelebrationDrawOp(
        Rect bounds,
        string word,
        SKTypeface typeface,
        List<ConfettiPiece> confetti,
        float elapsedSeconds) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public void Dispose() { }
        public bool Equals(ICustomDrawOperation? other) => false;
        public bool HitTest(Point p) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null)
            {
                return;
            }

            using ISkiaSharpApiLease lease = leaseFeature.Lease();
            SKCanvas canvas = lease.SkCanvas;
            canvas.Clear(SKColors.Transparent);

            float width = (float)bounds.Width;
            float height = (float)bounds.Height;

            using var confettiPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            foreach (ConfettiPiece p in confetti)
            {
                canvas.Save();
                canvas.Translate(p.X, p.Y);
                canvas.RotateDegrees(p.Angle);
                confettiPaint.Color = p.Color;
                canvas.DrawRect(-p.Width / 2f, -p.Height / 2f, p.Width, p.Height, confettiPaint);
                canvas.Restore();
            }

            float fontSize = CalculateFontSize(word, typeface, width * 0.85f);
            using var textMeasurePaint = new SKPaint
            {
                Typeface = typeface,
                TextSize = fontSize,
                IsAntialias = true
            };
            using var textPath = textMeasurePaint.GetTextPath(word, 0, 0);
            SKRect textBounds = textPath.Bounds;

            float offsetX = (width - textBounds.Width) / 2f - textBounds.Left;
            float offsetY = (height - textBounds.Height) / 2f - textBounds.Top;
            textPath.Offset(offsetX, offsetY);
            SKRect finalBounds = textPath.Bounds;

            using var strokePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 40f, // 20px visible white stroke outside the fill
                StrokeJoin = SKStrokeJoin.Round,
                StrokeCap = SKStrokeCap.Round,
                Color = SKColors.White
            };
            canvas.DrawPath(textPath, strokePaint);

            float shift = (elapsedSeconds * 0.6f) % 1f;
            SKColor[] shiftedColors = BuildShiftedColors(shift);
            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(finalBounds.Left - 20f, finalBounds.Top),
                new SKPoint(finalBounds.Right + 20f, finalBounds.Bottom),
                shiftedColors,
                null,
                SKShaderTileMode.Clamp);

            using var fillPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Shader = shader
            };
            canvas.DrawPath(textPath, fillPaint);
        }

        private static float CalculateFontSize(string text, SKTypeface typeface, float maxWidth)
        {
            float size = 220f;
            using var paint = new SKPaint { Typeface = typeface };
            while (size > 48f)
            {
                paint.TextSize = size;
                if (paint.MeasureText(text) <= maxWidth)
                {
                    return size;
                }
                size -= 12f;
            }
            return size;
        }

        private static SKColor[] BuildShiftedColors(float shift)
        {
            int count = NeonRainbow.Length;
            var colors = new SKColor[count];
            for (int i = 0; i < count; i++)
            {
                float t = (i / (float)(count - 1) + shift) % 1f;
                colors[i] = SampleRainbow(t);
            }
            return colors;
        }

        private static SKColor SampleRainbow(float t)
        {
            int segments = NeonRainbow.Length - 1;
            float scaled = t * segments;
            int idx = Math.Clamp((int)scaled, 0, segments - 1);
            float frac = scaled - idx;

            SKColor a = NeonRainbow[idx];
            SKColor b = NeonRainbow[idx + 1];

            return new SKColor(
                (byte)(a.Red + (b.Red - a.Red) * frac),
                (byte)(a.Green + (b.Green - a.Green) * frac),
                (byte)(a.Blue + (b.Blue - a.Blue) * frac));
        }
    }

    private sealed class ConfettiPiece
    {
        public float X;
        public float Y;
        public float Vx;
        public float Vy;
        public float Width;
        public float Height;
        public float Angle;
        public float Spin;
        public float SwayPhase;
        public SKColor Color;
    }
}

