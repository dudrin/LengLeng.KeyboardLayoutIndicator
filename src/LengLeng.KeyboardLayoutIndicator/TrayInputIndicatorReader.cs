using System.Globalization;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class TrayInputIndicatorReader
{
    private const int MaxClassNameLength = 256;
    public const int NormalizedWidth = 24;
    public const int NormalizedHeight = 9;
    private const int MinimumTextPixels = 20;
    private const double MinimumEnglishScore = 0.74;
    private const double MinimumManualEnglishScore = 0.62;
    private const double MinimumManualSearchEnglishScore = 0.70;
    private const int Srccopy = 0x00CC0020;
    private const int Captureblt = 0x40000000;
    private const int ManualSearchStepPx = 3;

    private Rectangle? _lastManualIndicatorRectangle;

    private static readonly string[] EnglishTemplate =
    {
        ".#########...###..#####.",
        ".###...####..###.###..##",
        ".###...####..###.##.....",
        ".###...#####.######.....",
        ".#################..####",
        ".###...###.########..###",
        ".###...###..#######..###",
        ".###...###..####.###.###",
        ".#########...###..#####."
    };

    public LayoutSnapshot GetCurrentLayout(IndicatorSettings? settings = null)
    {
        if (TryReadManualLayout(settings, out var manualLayout))
        {
            return manualLayout;
        }

        return GetCurrentLayoutFromSystemTray();
    }

    public static bool TryCaptureEnglishTemplate(
        Rectangle rectangle,
        out string[] template,
        out string? error)
    {
        template = Array.Empty<string>();
        error = null;

        if (rectangle.Width < 8 || rectangle.Height < 8)
        {
            error = "Выбранная область слишком маленькая.";
            return false;
        }

        var pixels = CaptureScreen(rectangle.Left, rectangle.Top, rectangle.Width, rectangle.Height);
        if (pixels.Length == 0)
        {
            error = "Не удалось прочитать пиксели выбранной области.";
            return false;
        }

        var mask = BuildTextMask(
            pixels,
            rectangle.Width,
            rectangle.Height,
            out var bounds,
            out var textPixelCount);
        if (textPixelCount < MinimumTextPixels || bounds == PixelBounds.Empty)
        {
            error = "В выбранной области не найден текст значка раскладки.";
            return false;
        }

        template = ToTemplate(NormalizeMask(mask, rectangle.Width, bounds));
        return true;
    }

    private bool TryReadManualLayout(IndicatorSettings? settings, out LayoutSnapshot layout)
    {
        layout = LayoutSnapshot.Unknown;
        if (settings?.ManualEnglishIndicatorRect is null
            || settings.ManualEnglishIndicatorTemplate is null)
        {
            return false;
        }

        var rectangle = settings.ManualEnglishIndicatorRect.ToRectangle();
        if (!settings.ManualEnglishIndicatorRect.IsValid
            || settings.ManualEnglishIndicatorTemplate.Length != NormalizedHeight)
        {
            return false;
        }

        var indicatorWindow = FindInputIndicatorButton();
        if (indicatorWindow != 0
            && GetWindowRect(indicatorWindow, out var inputIndicatorRect)
            && TryClassifyRectangle(
                inputIndicatorRect.ToRectangle(),
                settings.ManualEnglishIndicatorTemplate,
                MinimumManualEnglishScore,
                out layout,
                out _))
        {
            if (settings.IsEnglish(layout))
            {
                _lastManualIndicatorRectangle = inputIndicatorRect.ToRectangle();
            }

            return true;
        }

        if (_lastManualIndicatorRectangle is { } lastRectangle
            && TryClassifyRectangle(
                lastRectangle,
                settings.ManualEnglishIndicatorTemplate,
                MinimumManualEnglishScore,
                out layout,
                out var lastScore)
            && lastScore >= MinimumManualEnglishScore)
        {
            return true;
        }

        if (TryFindManualEnglishTemplate(settings, rectangle, out var matchRectangle, out var bestScore))
        {
            _lastManualIndicatorRectangle = matchRectangle;
            layout = new LayoutSnapshot(true, "en-US", "en", 0x0409, 0);
            return true;
        }

        if (bestScore >= 0)
        {
            layout = new LayoutSnapshot(true, "tray-non-english", "other", 0, 0);
            return true;
        }

        return TryClassifyRectangle(
            rectangle,
            settings.ManualEnglishIndicatorTemplate,
            MinimumManualEnglishScore,
            out layout,
            out _);
    }

    private static LayoutSnapshot GetCurrentLayoutFromSystemTray()
    {
        var indicatorWindow = FindInputIndicatorButton();
        if (indicatorWindow == 0 || !GetWindowRect(indicatorWindow, out var rect))
        {
            return LayoutSnapshot.Unknown;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 16 || height < 16)
        {
            return LayoutSnapshot.Unknown;
        }

        var pixels = CaptureScreen(rect.Left, rect.Top, width, height);
        if (pixels.Length == 0)
        {
            return LayoutSnapshot.Unknown;
        }

        var mask = BuildTextMask(pixels, width, height, out var bounds, out var textPixelCount);
        if (textPixelCount < MinimumTextPixels)
        {
            return LayoutSnapshot.Unknown;
        }

        var normalized = NormalizeMask(mask, width, bounds);
        var englishScore = Score(EnglishTemplate, normalized);
        return englishScore >= MinimumEnglishScore
            ? new LayoutSnapshot(true, "en-US", "en", 0x0409, 0)
            : new LayoutSnapshot(true, "tray-non-english", "other", 0, 0);
    }

    private static bool TryClassifyRectangle(
        Rectangle rectangle,
        string[] template,
        double minimumEnglishScore,
        out LayoutSnapshot layout,
        out double englishScore)
    {
        layout = LayoutSnapshot.Unknown;
        englishScore = -1;

        if (rectangle.Width < 8 || rectangle.Height < 8)
        {
            return false;
        }

        var pixels = CaptureScreen(rectangle.Left, rectangle.Top, rectangle.Width, rectangle.Height);
        if (pixels.Length == 0)
        {
            return false;
        }

        var mask = BuildTextMask(
            pixels,
            rectangle.Width,
            rectangle.Height,
            out var bounds,
            out var textPixelCount);
        if (textPixelCount < MinimumTextPixels || bounds == PixelBounds.Empty)
        {
            return false;
        }

        var normalized = NormalizeMask(mask, rectangle.Width, bounds);
        englishScore = Score(template, normalized);
        layout = englishScore >= minimumEnglishScore
            ? new LayoutSnapshot(true, "en-US", "en", 0x0409, 0)
            : new LayoutSnapshot(true, "tray-non-english", "other", 0, 0);
        return true;
    }

    private static bool TryFindManualEnglishTemplate(
        IndicatorSettings settings,
        Rectangle originalRectangle,
        out Rectangle matchRectangle,
        out double bestScore)
    {
        matchRectangle = Rectangle.Empty;
        bestScore = -1;

        if (settings.ManualEnglishIndicatorTemplate is null)
        {
            return false;
        }

        foreach (var searchRectangle in EnumerateManualSearchRectangles(
            originalRectangle,
            settings.ManualEnglishIndicatorSearchRadiusPx))
        {
            if (TryFindManualEnglishTemplateInRectangle(
                searchRectangle,
                originalRectangle.Size,
                settings.ManualEnglishIndicatorTemplate,
                out var currentMatch,
                out var currentScore))
            {
                if (currentScore > bestScore)
                {
                    bestScore = currentScore;
                    matchRectangle = currentMatch;
                }
            }
            else if (currentScore > bestScore)
            {
                bestScore = currentScore;
            }
        }

        return bestScore >= MinimumManualSearchEnglishScore;
    }

    private static bool TryFindManualEnglishTemplateInRectangle(
        Rectangle searchRectangle,
        Size candidateSize,
        string[] template,
        out Rectangle matchRectangle,
        out double bestScore)
    {
        matchRectangle = Rectangle.Empty;
        bestScore = -1;

        searchRectangle = NormalizeSearchRectangle(searchRectangle, candidateSize);
        if (searchRectangle.Width < candidateSize.Width || searchRectangle.Height < candidateSize.Height)
        {
            return false;
        }

        var pixels = CaptureScreen(
            searchRectangle.Left,
            searchRectangle.Top,
            searchRectangle.Width,
            searchRectangle.Height);
        if (pixels.Length == 0)
        {
            return false;
        }

        for (var y = 0; y <= searchRectangle.Height - candidateSize.Height; y += ManualSearchStepPx)
        {
            for (var x = 0; x <= searchRectangle.Width - candidateSize.Width; x += ManualSearchStepPx)
            {
                if (!TryNormalizeCandidate(
                    pixels,
                    searchRectangle.Width,
                    x,
                    y,
                    candidateSize.Width,
                    candidateSize.Height,
                    out var normalized))
                {
                    continue;
                }

                var score = Score(template, normalized);
                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                matchRectangle = new Rectangle(
                    searchRectangle.Left + x,
                    searchRectangle.Top + y,
                    candidateSize.Width,
                    candidateSize.Height);
            }
        }

        return bestScore >= MinimumManualSearchEnglishScore;
    }

    private static bool TryNormalizeCandidate(
        byte[] sourcePixels,
        int sourceWidth,
        int sourceX,
        int sourceY,
        int width,
        int height,
        out bool[] normalized)
    {
        normalized = Array.Empty<bool>();
        var candidatePixels = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(
                sourcePixels,
                ((sourceY + y) * sourceWidth + sourceX) * 4,
                candidatePixels,
                y * width * 4,
                width * 4);
        }

        var mask = BuildTextMask(
            candidatePixels,
            width,
            height,
            out var bounds,
            out var textPixelCount);
        if (textPixelCount < MinimumTextPixels || bounds == PixelBounds.Empty)
        {
            return false;
        }

        normalized = NormalizeMask(mask, width, bounds);
        return true;
    }

    private static IEnumerable<Rectangle> EnumerateManualSearchRectangles(
        Rectangle originalRectangle,
        int searchRadius)
    {
        var rectangles = new List<Rectangle>();

        foreach (var trayWindow in EnumerateTrayWindows())
        {
            if (GetWindowRect(trayWindow, out var trayRect))
            {
                rectangles.Add(ExpandAroundOriginal(
                    trayRect.ToRectangle(),
                    originalRectangle,
                    searchRadius));
            }

            EnumChildWindows(
                trayWindow,
                (childWindow, _) =>
                {
                    var className = GetWindowClassName(childWindow);
                    if ((string.Equals(className, "TrayNotifyWnd", StringComparison.Ordinal)
                            || string.Equals(className, "InputIndicatorButton", StringComparison.Ordinal))
                        && IsWindowVisible(childWindow)
                        && GetWindowRect(childWindow, out var childRect))
                    {
                        rectangles.Add(InflateRectangle(childRect.ToRectangle(), searchRadius / 3, 12));
                    }

                    return true;
                },
                0);
        }

        rectangles.Add(InflateRectangle(originalRectangle, searchRadius, 40));

        return rectangles
            .Select(rectangle => NormalizeSearchRectangle(rectangle, originalRectangle.Size))
            .Where(rectangle => rectangle.Width >= originalRectangle.Width
                && rectangle.Height >= originalRectangle.Height)
            .Distinct()
            .ToArray();
    }

    private static Rectangle ExpandAroundOriginal(
        Rectangle trayRectangle,
        Rectangle originalRectangle,
        int searchRadius)
    {
        if (trayRectangle.Width >= trayRectangle.Height)
        {
            return Rectangle.FromLTRB(
                Math.Max(trayRectangle.Left, originalRectangle.Left - searchRadius),
                trayRectangle.Top,
                Math.Min(trayRectangle.Right, originalRectangle.Right + searchRadius),
                trayRectangle.Bottom);
        }

        return Rectangle.FromLTRB(
            trayRectangle.Left,
            Math.Max(trayRectangle.Top, originalRectangle.Top - searchRadius),
            trayRectangle.Right,
            Math.Min(trayRectangle.Bottom, originalRectangle.Bottom + searchRadius));
    }

    private static Rectangle NormalizeSearchRectangle(Rectangle rectangle, Size candidateSize)
    {
        return Rectangle.FromLTRB(
            rectangle.Left,
            rectangle.Top,
            Math.Max(rectangle.Right, rectangle.Left + candidateSize.Width),
            Math.Max(rectangle.Bottom, rectangle.Top + candidateSize.Height));
    }

    private static Rectangle InflateRectangle(Rectangle rectangle, int width, int height)
    {
        rectangle.Inflate(width, height);
        return rectangle;
    }

    private static IEnumerable<nint> EnumerateTrayWindows()
    {
        var trayWindows = new List<nint>();
        EnumWindows(
            (window, _) =>
            {
                var className = GetWindowClassName(window);
                if ((string.Equals(className, "Shell_TrayWnd", StringComparison.Ordinal)
                        || string.Equals(className, "Shell_SecondaryTrayWnd", StringComparison.Ordinal))
                    && IsWindowVisible(window))
                {
                    trayWindows.Add(window);
                }

                return true;
            },
            0);

        return trayWindows;
    }

    private static nint FindInputIndicatorButton()
    {
        var shellTray = FindWindow("Shell_TrayWnd", null);
        if (shellTray == 0)
        {
            return 0;
        }

        nint result = 0;
        EnumChildWindows(
            shellTray,
            (window, _) =>
            {
                var className = GetWindowClassName(window);
                if (string.Equals(className, "InputIndicatorButton", StringComparison.Ordinal)
                    && IsWindowVisible(window))
                {
                    result = window;
                    return false;
                }

                return true;
            },
            0);

        return result;
    }

    private static byte[] CaptureScreen(int left, int top, int width, int height)
    {
        nint screenDc = 0;
        nint memoryDc = 0;
        nint bitmap = 0;
        nint previous = 0;

        try
        {
            screenDc = GetDC(0);
            if (screenDc == 0)
            {
                return Array.Empty<byte>();
            }

            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (memoryDc == 0 || bitmap == 0)
            {
                return Array.Empty<byte>();
            }

            previous = SelectObject(memoryDc, bitmap);
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, left, top, Srccopy | Captureblt))
            {
                return Array.Empty<byte>();
            }

            var bitmapInfo = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0
                }
            };

            var pixels = new byte[width * height * 4];
            return GetDIBits(screenDc, bitmap, 0, unchecked((uint)height), pixels, ref bitmapInfo, 0) == 0
                ? Array.Empty<byte>()
                : pixels;
        }
        finally
        {
            if (previous != 0 && memoryDc != 0)
            {
                SelectObject(memoryDc, previous);
            }

            if (bitmap != 0)
            {
                DeleteObject(bitmap);
            }

            if (memoryDc != 0)
            {
                DeleteDC(memoryDc);
            }

            if (screenDc != 0)
            {
                ReleaseDC(0, screenDc);
            }
        }
    }

    private static bool[] BuildTextMask(
        byte[] pixels,
        int width,
        int height,
        out PixelBounds bounds,
        out int textPixelCount)
    {
        var background = EstimateBackground(pixels, width, height);
        var mask = new bool[width * height];

        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        textPixelCount = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * 4;
                var b = pixels[index];
                var g = pixels[index + 1];
                var r = pixels[index + 2];

                if (ColorDistance(r, g, b, background.R, background.G, background.B) < 45)
                {
                    continue;
                }

                mask[y * width + x] = true;
                textPixelCount++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        bounds = maxX < minX || maxY < minY
            ? PixelBounds.Empty
            : new PixelBounds(minX, minY, maxX, maxY);

        return mask;
    }

    private static PixelColor EstimateBackground(byte[] pixels, int width, int height)
    {
        var samples = new[]
        {
            PixelAt(pixels, width, 0, 0),
            PixelAt(pixels, width, Math.Max(0, width - 1), 0),
            PixelAt(pixels, width, 0, Math.Max(0, height - 1)),
            PixelAt(pixels, width, Math.Max(0, width - 1), Math.Max(0, height - 1))
        };

        return new PixelColor(
            unchecked((byte)samples.Average(color => color.R)),
            unchecked((byte)samples.Average(color => color.G)),
            unchecked((byte)samples.Average(color => color.B)));
    }

    private static PixelColor PixelAt(byte[] pixels, int width, int x, int y)
    {
        var index = (y * width + x) * 4;
        return new PixelColor(pixels[index + 2], pixels[index + 1], pixels[index]);
    }

    private static bool[] NormalizeMask(bool[] mask, int sourceWidth, PixelBounds bounds)
    {
        var normalized = new bool[NormalizedWidth * NormalizedHeight];
        var textWidth = bounds.MaxX - bounds.MinX + 1;
        var textHeight = bounds.MaxY - bounds.MinY + 1;

        for (var y = 0; y < NormalizedHeight; y++)
        {
            for (var x = 0; x < NormalizedWidth; x++)
            {
                var sourceXStart = bounds.MinX + x * textWidth / NormalizedWidth;
                var sourceXEnd = bounds.MinX + ((x + 1) * textWidth / NormalizedWidth) - 1;
                var sourceYStart = bounds.MinY + y * textHeight / NormalizedHeight;
                var sourceYEnd = bounds.MinY + ((y + 1) * textHeight / NormalizedHeight) - 1;

                var total = 0;
                var set = 0;
                for (var sourceY = sourceYStart; sourceY <= sourceYEnd; sourceY++)
                {
                    for (var sourceX = sourceXStart; sourceX <= sourceXEnd; sourceX++)
                    {
                        total++;
                        if (mask[sourceY * sourceWidth + sourceX])
                        {
                            set++;
                        }
                    }
                }

                normalized[y * NormalizedWidth + x] = set >= Math.Max(1, unchecked((int)(total * 0.18)));
            }
        }

        return normalized;
    }

    private static double Score(string[] template, bool[] normalized)
    {
        var intersection = 0;
        var union = 0;

        for (var y = 0; y < NormalizedHeight; y++)
        {
            for (var x = 0; x < NormalizedWidth; x++)
            {
                var expected = y < template.Length
                    && x < template[y].Length
                    && template[y][x] == '#';
                var actual = normalized[y * NormalizedWidth + x];

                if (expected && actual)
                {
                    intersection++;
                }

                if (expected || actual)
                {
                    union++;
                }
            }
        }

        return union == 0 ? 0 : (double)intersection / union;
    }

    private static string[] ToTemplate(bool[] normalized)
    {
        var lines = new string[NormalizedHeight];
        for (var y = 0; y < NormalizedHeight; y++)
        {
            var builder = new StringBuilder(NormalizedWidth);
            for (var x = 0; x < NormalizedWidth; x++)
            {
                builder.Append(normalized[y * NormalizedWidth + x] ? '#' : '.');
            }

            lines[y] = builder.ToString();
        }

        return lines;
    }

    private static double ColorDistance(int r1, int g1, int b1, int r2, int g2, int b2)
    {
        var r = r1 - r2;
        var g = g1 - g2;
        var b = b1 - b2;
        return Math.Sqrt(r * r + g * g + b * b);
    }

    private static string GetWindowClassName(nint window)
    {
        var className = new StringBuilder(MaxClassNameLength);
        return GetClassName(window, className, className.Capacity) == 0
            ? string.Empty
            : className.ToString();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumChildProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint deviceContext, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint deviceContext, nint handle);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        nint destinationDeviceContext,
        int x,
        int y,
        int width,
        int height,
        nint sourceDeviceContext,
        int sourceX,
        int sourceY,
        int rasterOperation);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        nint deviceContext,
        nint bitmap,
        uint startScan,
        uint scanLines,
        byte[] bits,
        ref BitmapInfo bitmapInfo,
        uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint deviceContext);

    private delegate bool EnumChildProc(nint window, nint parameter);

    private readonly record struct PixelBounds(int MinX, int MinY, int MaxX, int MaxY)
    {
        public static PixelBounds Empty { get; } = new(0, 0, -1, -1);
    }

    private readonly record struct PixelColor(byte R, byte G, byte B);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public Rectangle ToRectangle()
        {
            return Rectangle.FromLTRB(Left, Top, Right, Bottom);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }
}
