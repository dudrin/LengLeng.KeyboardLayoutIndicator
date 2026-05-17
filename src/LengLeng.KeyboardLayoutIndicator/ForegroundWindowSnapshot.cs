using System.Drawing;

namespace LengLeng.KeyboardLayoutIndicator;

internal readonly record struct ForegroundWindowSnapshot(
    nint Handle,
    Rectangle Bounds,
    bool BlocksLowerIntegrityInput)
{
    public static ForegroundWindowSnapshot Empty { get; } = new(0, Rectangle.Empty, false);
}
