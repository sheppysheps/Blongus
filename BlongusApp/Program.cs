using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BlongusApp;

internal static class Program
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;
    private const uint FileTypeDisk = 0x0001;
    private const uint FileTypePipe = 0x0003;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(IntPtr hFile);

    [STAThread]
    private static int Main(string[] args)
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
            a.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/?"));

        if (showHelp)
        {
            EnsureConsoleAttached();
            Console.WriteLine("Blongus - Generates a nonsense word from three dictionary parts.");
            Console.WriteLine("Usage: Blongus.exe [--cli | --gui | --help]");
            Console.WriteLine("  --cli, -c   Print the generated word to the console and exit.");
            Console.WriteLine("  --gui, -g   Show the 5-second confetti celebration overlay (default when launched as GUI).");
            return 0;
        }

        if (forceCli || (!forceGui && IsStdoutActuallyPipedOrFile()))
        {
            if (!IsStdoutActuallyPipedOrFile())
            {
                EnsureConsoleAttached();
            }

            Console.WriteLine(BlongusGenerator.Generate());
            Console.Out.Flush();
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new CelebrationOverlayForm(BlongusGenerator.Generate()));
        return 0;
    }

    private static bool IsStdoutActuallyPipedOrFile()
    {
        IntPtr handle = GetStdHandle(StdOutputHandle);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return false;
        }

        uint type = GetFileType(handle);
        return type == FileTypeDisk || type == FileTypePipe;
    }

    private static void EnsureConsoleAttached()
    {
        if (AttachConsole(AttachParentProcess))
        {
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdout);
        }
    }
}

internal static class BlongusGenerator
{
    private static readonly Assembly CurrentAssembly = Assembly.GetExecutingAssembly();

    public static string Generate()
    {
        var first = ReadLines("Blongus.first.txt");
        var second = ReadLines("Blongus.second.txt");

        string word = PickRandom(first) + PickRandom(second);

        // 1 in 8 chance that the third part is omitted (matches blongus.py)
        if (Random.Shared.Next(8) != 0)
        {
            var third = ReadLines("Blongus.third.txt");
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

internal sealed class CelebrationOverlayForm : Form
{
    private const int WsExLayered = 0x80000;
    private const int WsExTransparent = 0x20;
    private const int WsExTopMost = 0x8;
    private const int WsExToolWindow = 0x80;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const int UlwAlpha = 0x02;

    private static readonly Color[] NeonRainbow =
    [
        Color.FromArgb(255, 255, 20, 147),  // Neon Pink
        Color.FromArgb(255, 255, 59, 0),    // Neon Red-Orange
        Color.FromArgb(255, 255, 255, 0),   // Neon Yellow
        Color.FromArgb(255, 57, 255, 20),   // Neon Lime
        Color.FromArgb(255, 0, 255, 255),   // Neon Cyan
        Color.FromArgb(255, 0, 128, 255),   // Neon Electric Blue
        Color.FromArgb(255, 191, 0, 255),   // Neon Violet
        Color.FromArgb(255, 255, 20, 147),  // Wrap back to Neon Pink
    ];

    private readonly string _word;
    private readonly List<ConfettiPiece> _confetti = [];
    private readonly System.Windows.Forms.Timer _frameTimer;
    private readonly System.Windows.Forms.Timer _closeTimer;
    private readonly DateTime _startTime;
    private SoundPlayer? _soundPlayer;
    private Stream? _soundStream;

    public CelebrationOverlayForm(string word)
    {
        _word = word;
        _startTime = DateTime.UtcNow;

        Rectangle bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        ShowInTaskbar = false;
        TopMost = true;

        InitConfetti(bounds.Width, bounds.Height);
        PlayCheeringSound();

        _frameTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _frameTimer.Tick += (_, _) =>
        {
            UpdateConfetti(bounds.Width, bounds.Height);
            RenderLayeredFrame();
        };

        _closeTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _closeTimer.Tick += (_, _) => Close();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WsExLayered | WsExTransparent | WsExTopMost | WsExToolWindow;
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RenderLayeredFrame();
        _frameTimer.Start();
        _closeTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _frameTimer.Dispose();
            _closeTimer.Dispose();
            _soundPlayer?.Stop();
            _soundPlayer?.Dispose();
            _soundStream?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void PlayCheeringSound()
    {
        _soundStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Blongus.children_cheering.wav");
        if (_soundStream is not null)
        {
            _soundPlayer = new SoundPlayer(_soundStream);
            _soundPlayer.Play();
        }
    }

    private void InitConfetti(int width, int height)
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
                Color = NeonRainbow[Random.Shared.Next(NeonRainbow.Length - 1)],
            });
        }
    }

    private void UpdateConfetti(int width, int height)
    {
        foreach (ConfettiPiece piece in _confetti)
        {
            piece.SwayPhase += 0.08f;
            piece.X += piece.Vx + MathF.Sin(piece.SwayPhase) * 1.8f;
            piece.Y += piece.Vy;
            piece.Angle += piece.Spin;

            if (piece.Y > height + 30)
            {
                piece.Y = -20f;
                piece.X = Random.Shared.NextSingle() * width;
            }
        }
    }

    private void RenderLayeredFrame()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            DrawConfetti(g);
            DrawWord(g);
        }

        ApplyBitmapToLayeredWindow(bitmap);
    }

    private void DrawConfetti(Graphics g)
    {
        foreach (ConfettiPiece piece in _confetti)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(piece.X, piece.Y);
            g.RotateTransform(piece.Angle);

            using var brush = new SolidBrush(piece.Color);
            g.FillRectangle(brush, -piece.Width / 2f, -piece.Height / 2f, piece.Width, piece.Height);
            g.Restore(state);
        }
    }

    private void DrawWord(Graphics g)
    {
        using var fontFamily = new FontFamily("Comic Sans MS");
        float fontSize = CalculateFontSize(g, fontFamily, _word, Width * 0.85f);

        using var path = new GraphicsPath();
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        var layoutRect = new RectangleF(0, 0, Width, Height);
        path.AddString(_word, fontFamily, (int)FontStyle.Bold, fontSize, layoutRect, format);

        // 40px centered stroke = 20px visible white outline around the filled letters
        using var strokePen = new Pen(Color.White, 40f)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawPath(strokePen, path);

        RectangleF bounds = path.GetBounds();
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            float elapsed = (float)(DateTime.UtcNow - _startTime).TotalSeconds;
            float shift = (elapsed * 0.6f) % 1f;

            using var rainbowBrush = new LinearGradientBrush(
                new PointF(bounds.Left - 20f, bounds.Top),
                new PointF(bounds.Right + 20f, bounds.Bottom),
                NeonRainbow[0],
                NeonRainbow[^1]);

            rainbowBrush.InterpolationColors = BuildShiftedRainbowBlend(shift);
            g.FillPath(rainbowBrush, path);
        }
    }

    private static ColorBlend BuildShiftedRainbowBlend(float shift)
    {
        int count = NeonRainbow.Length;
        var colors = new Color[count];
        var positions = new float[count];

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            positions[i] = t;
            colors[i] = SampleRainbow((t + shift) % 1f);
        }

        return new ColorBlend { Colors = colors, Positions = positions };
    }

    private static Color SampleRainbow(float t)
    {
        int segments = NeonRainbow.Length - 1;
        float scaled = t * segments;
        int idx = Math.Clamp((int)scaled, 0, segments - 1);
        float frac = scaled - idx;

        Color a = NeonRainbow[idx];
        Color b = NeonRainbow[idx + 1];

        return Color.FromArgb(
            255,
            (int)(a.R + (b.R - a.R) * frac),
            (int)(a.G + (b.G - a.G) * frac),
            (int)(a.B + (b.B - a.B) * frac));
    }

    private static float CalculateFontSize(Graphics g, FontFamily family, string text, float maxWidth)
    {
        float size = 220f;
        while (size > 48f)
        {
            using var path = new GraphicsPath();
            path.AddString(text, family, (int)FontStyle.Bold, size, PointF.Empty, StringFormat.GenericDefault);
            if (path.GetBounds().Width <= maxWidth)
            {
                return size;
            }
            size -= 12f;
        }
        return size;
    }

    private void ApplyBitmapToLayeredWindow(Bitmap bitmap)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            oldBitmap = SelectObject(memDc, hBitmap);

            var size = new NativeSize(bitmap.Width, bitmap.Height);
            var pointSource = new NativePoint(0, 0);
            var topPos = new NativePoint(Left, Top);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };

            UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size, memDc, ref pointSource, 0, ref blend, UlwAlpha);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            if (hBitmap != IntPtr.Zero)
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
            }
            DeleteDC(memDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize(int cx, int cy)
    {
        public int Cx = cx;
        public int Cy = cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref NativePoint pptDst,
        ref NativeSize psize,
        IntPtr hdcSrc,
        ref NativePoint pprSrc,
        int crKey,
        ref BlendFunction pblend,
        int dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

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
        public Color Color;
    }
}

