using System.Runtime.InteropServices;

namespace LengLeng.KeyboardLayoutIndicator;

internal static class KeyboardInputGuard
{
    private const int VirtualKeyShift = 0x10;
    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyMenu = 0x12;
    private const int VirtualKeyLeftWindows = 0x5B;
    private const int VirtualKeyRightWindows = 0x5C;

    public static bool IsModifierDown()
    {
        return IsKeyDown(VirtualKeyShift)
            || IsKeyDown(VirtualKeyControl)
            || IsKeyDown(VirtualKeyMenu)
            || IsKeyDown(VirtualKeyLeftWindows)
            || IsKeyDown(VirtualKeyRightWindows);
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
