using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class LockKeyUserInputHook : IDisposable
{
    private const int WhKeyboardLowLevel = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x00000010;

    private readonly HookProc _hookProc;
    private nint _hook;
    private ushort _watchedVirtualKey;
    private bool _physicalKeyDown;

    public LockKeyUserInputHook()
    {
        _hookProc = HandleKeyboard;
    }

    public Func<bool>? BeforePhysicalKeyDown { get; set; }

    public Action? AfterPhysicalKeyUp { get; set; }

    public Action<uint>? BeforePhysicalNonIndicatorKeyDown { get; set; }

    public void Start()
    {
        if (_hook != 0)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = module?.ModuleName is null ? 0 : GetModuleHandle(module.ModuleName);
        _hook = SetWindowsHookEx(WhKeyboardLowLevel, _hookProc, moduleHandle, 0);
        if (_hook == 0)
        {
            FileLog.Write("agent", $"Cannot install keyboard hook. Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    public void SetWatchedVirtualKey(ushort virtualKey)
    {
        _watchedVirtualKey = virtualKey;
        _physicalKeyDown = false;
    }

    public void Dispose()
    {
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private nint HandleKeyboard(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _watchedVirtualKey != 0)
        {
            var data = Marshal.PtrToStructure<KeyboardLowLevelHookStruct>(lParam);
            var message = unchecked((int)wParam);
            var isInjected = (data.Flags & LlkhfInjected) != 0;
            var isKeyDown = message == WmKeyDown || message == WmSysKeyDown;
            var isKeyUp = message == WmKeyUp || message == WmSysKeyUp;

            if (!isInjected && isKeyDown && data.VirtualKeyCode != _watchedVirtualKey)
            {
                BeforePhysicalNonIndicatorKeyDown?.Invoke(data.VirtualKeyCode);
            }

            if (data.VirtualKeyCode == _watchedVirtualKey
                && !isInjected)
            {
                if (isKeyDown && !_physicalKeyDown)
                {
                    _physicalKeyDown = true;
                    if (BeforePhysicalKeyDown?.Invoke() == true)
                    {
                        return 1;
                    }
                }
                else if (isKeyUp)
                {
                    _physicalKeyDown = false;
                    Task.Delay(60).ContinueWith(
                        _ => AfterPhysicalKeyUp?.Invoke(),
                        TaskScheduler.Default);
                    return 1;
                }
                else if (isKeyDown)
                {
                    return 1;
                }
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hookId, HookProc hookProc, nint instance, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string moduleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardLowLevelHookStruct
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
}
