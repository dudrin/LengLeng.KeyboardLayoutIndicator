using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class TaskbarHoverDetector
{
    private const int MaxClassNameLength = 256;

    private static readonly string[] TaskbarClassNames =
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    private static readonly string[] PreviewClassNameFragments =
    {
        "Thumbnail",
        "TaskList"
    };

    public bool IsCursorOverTaskbarOrPreview(int previewBandPixels)
    {
        if (!GetCursorPos(out var point))
        {
            return false;
        }

        var cursor = new Point(point.X, point.Y);
        if (IsPointOverKnownShellWindow(cursor))
        {
            return true;
        }

        foreach (var taskbarRect in EnumerateTaskbarRects())
        {
            if (taskbarRect.Contains(cursor))
            {
                return true;
            }

            if (ExpandTowardDesktop(taskbarRect, previewBandPixels).Contains(cursor))
            {
                return true;
            }
        }

        return IsPointOverKnownPreviewWindow(cursor);
    }

    private static bool IsPointOverKnownShellWindow(Point point)
    {
        var window = WindowFromPoint(point);
        while (window != 0)
        {
            var className = GetWindowClassName(window);
            if (TaskbarClassNames.Contains(className, StringComparer.Ordinal)
                || className.Contains("TaskList", StringComparison.OrdinalIgnoreCase)
                || className.Contains("Thumbnail", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            window = GetParent(window);
        }

        return false;
    }

    private static bool IsPointOverKnownPreviewWindow(Point point)
    {
        var result = false;
        EnumWindows(
            (window, _) =>
            {
                if (!IsWindowVisible(window))
                {
                    return true;
                }

                var className = GetWindowClassName(window);
                if (!PreviewClassNameFragments.Any(fragment =>
                    className.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                if (GetWindowRect(window, out var rect)
                    && rect.ToRectangle().Contains(point))
                {
                    result = true;
                    return false;
                }

                return true;
            },
            0);

        return result;
    }

    private static IEnumerable<Rectangle> EnumerateTaskbarRects()
    {
        var rectangles = new List<Rectangle>();
        EnumWindows(
            (window, _) =>
            {
                var className = GetWindowClassName(window);
                if (TaskbarClassNames.Contains(className, StringComparer.Ordinal)
                    && IsWindowVisible(window)
                    && GetWindowRect(window, out var rect))
                {
                    var rectangle = rect.ToRectangle();
                    if (rectangle.Width > 0 && rectangle.Height > 0)
                    {
                        rectangles.Add(rectangle);
                    }
                }

                return true;
            },
            0);

        return rectangles;
    }

    private static Rectangle ExpandTowardDesktop(Rectangle taskbarRect, int previewBandPixels)
    {
        if (previewBandPixels <= 0)
        {
            return taskbarRect;
        }

        var screenBounds = Screen.FromRectangle(taskbarRect).Bounds;
        var distances = new[]
        {
            (Edge: TaskbarEdge.Left, Distance: Math.Abs(taskbarRect.Left - screenBounds.Left)),
            (Edge: TaskbarEdge.Top, Distance: Math.Abs(taskbarRect.Top - screenBounds.Top)),
            (Edge: TaskbarEdge.Right, Distance: Math.Abs(screenBounds.Right - taskbarRect.Right)),
            (Edge: TaskbarEdge.Bottom, Distance: Math.Abs(screenBounds.Bottom - taskbarRect.Bottom))
        };

        var edge = distances.OrderBy(item => item.Distance).First().Edge;
        return edge switch
        {
            TaskbarEdge.Left => Rectangle.FromLTRB(
                taskbarRect.Left,
                taskbarRect.Top,
                Math.Min(screenBounds.Right, taskbarRect.Right + previewBandPixels),
                taskbarRect.Bottom),
            TaskbarEdge.Top => Rectangle.FromLTRB(
                taskbarRect.Left,
                taskbarRect.Top,
                taskbarRect.Right,
                Math.Min(screenBounds.Bottom, taskbarRect.Bottom + previewBandPixels)),
            TaskbarEdge.Right => Rectangle.FromLTRB(
                Math.Max(screenBounds.Left, taskbarRect.Left - previewBandPixels),
                taskbarRect.Top,
                taskbarRect.Right,
                taskbarRect.Bottom),
            _ => Rectangle.FromLTRB(
                taskbarRect.Left,
                Math.Max(screenBounds.Top, taskbarRect.Top - previewBandPixels),
                taskbarRect.Right,
                taskbarRect.Bottom)
        };
    }

    private static string GetWindowClassName(nint window)
    {
        var className = new StringBuilder(MaxClassNameLength);
        return GetClassName(window, className, className.Capacity) == 0
            ? string.Empty
            : className.ToString();
    }

    private enum TaskbarEdge
    {
        Left,
        Top,
        Right,
        Bottom
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int maxCount);

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        private readonly int _left;
        private readonly int _top;
        private readonly int _right;
        private readonly int _bottom;

        public Rectangle ToRectangle()
        {
            return Rectangle.FromLTRB(_left, _top, _right, _bottom);
        }
    }
}
