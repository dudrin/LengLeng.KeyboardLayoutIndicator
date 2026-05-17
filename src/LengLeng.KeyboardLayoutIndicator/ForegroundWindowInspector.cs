using System.Drawing;
using System.Runtime.InteropServices;

namespace LengLeng.KeyboardLayoutIndicator;

internal static class ForegroundWindowInspector
{
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int SecurityMandatoryMediumRid = 0x2000;
    private const int DwmwaExtendedFrameBounds = 9;

    private static readonly int CurrentProcessIntegrityRid = GetCurrentProcessIntegrityRid();

    public static ForegroundWindowSnapshot GetCurrent()
    {
        var window = GetForegroundWindow();
        if (window == 0)
        {
            return ForegroundWindowSnapshot.Empty;
        }

        var bounds = TryGetWindowBounds(window, out var rectangle)
            ? rectangle
            : Rectangle.Empty;

        var blocksLowerIntegrityInput = BlocksLowerIntegrityInput(window);
        return new ForegroundWindowSnapshot(window, bounds, blocksLowerIntegrityInput);
    }

    private static bool BlocksLowerIntegrityInput(nint window)
    {
        var threadId = GetWindowThreadProcessId(window, out var processId);
        if (threadId == 0 || processId == 0 || processId == Environment.ProcessId)
        {
            return false;
        }

        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return true;
        }

        try
        {
            if (!TryGetProcessIntegrityRid(process, out var targetIntegrityRid))
            {
                return true;
            }

            return targetIntegrityRid > CurrentProcessIntegrityRid
                && CurrentProcessIntegrityRid >= SecurityMandatoryMediumRid;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static int GetCurrentProcessIntegrityRid()
    {
        return TryGetProcessIntegrityRid(GetCurrentProcess(), out var integrityRid)
            ? integrityRid
            : SecurityMandatoryMediumRid;
    }

    private static bool TryGetProcessIntegrityRid(nint process, out int integrityRid)
    {
        integrityRid = SecurityMandatoryMediumRid;
        if (!OpenProcessToken(process, TokenQuery, out var token))
        {
            return false;
        }

        try
        {
            GetTokenInformation(token, TokenIntegrityLevel, 0, 0, out var length);
            if (length <= 0)
            {
                return false;
            }

            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out _))
                {
                    return false;
                }

                var tokenMandatoryLabel = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
                var subAuthorityCountPointer = GetSidSubAuthorityCount(tokenMandatoryLabel.Label.Sid);
                if (subAuthorityCountPointer == 0)
                {
                    return false;
                }

                var subAuthorityCount = Marshal.ReadByte(subAuthorityCountPointer);
                if (subAuthorityCount == 0)
                {
                    return false;
                }

                var ridPointer = GetSidSubAuthority(
                    tokenMandatoryLabel.Label.Sid,
                    unchecked((uint)(subAuthorityCount - 1)));

                integrityRid = Marshal.ReadInt32(ridPointer);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static bool TryGetWindowBounds(nint window, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (DwmGetWindowAttribute(
                window,
                DwmwaExtendedFrameBounds,
                out var frameBounds,
                Marshal.SizeOf<Rect>()) == 0
            && frameBounds.IsValid)
        {
            bounds = frameBounds.ToRectangle();
            return true;
        }

        if (GetWindowRect(window, out var rect) && rect.IsValid)
        {
            bounds = rect.ToRectangle();
            return true;
        }

        return false;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        int attribute,
        out Rect attributeValue,
        int attributeSize);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint process, int desiredAccess, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        nint token,
        int tokenInformationClass,
        nint tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthority(nint sid, uint subAuthority);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthorityCount(nint sid);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TokenMandatoryLabel
    {
        public readonly SidAndAttributes Label;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SidAndAttributes
    {
        public readonly nint Sid;
        public readonly int Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public bool IsValid => Right > Left && Bottom > Top;

        public Rectangle ToRectangle()
        {
            return Rectangle.FromLTRB(Left, Top, Right, Bottom);
        }
    }
}
