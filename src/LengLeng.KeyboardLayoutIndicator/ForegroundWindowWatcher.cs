using System.Runtime.InteropServices;

namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class ForegroundWindowWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;

    private readonly Action _onForegroundChanged;
    private readonly WinEventProc _callback;
    private nint _hook;

    public ForegroundWindowWatcher(Action onForegroundChanged)
    {
        _onForegroundChanged = onForegroundChanged;
        _callback = HandleWinEvent;
    }

    public void Start()
    {
        if (_hook != 0)
        {
            return;
        }

        _hook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            0,
            _callback,
            0,
            0,
            WinEventOutOfContext | WinEventSkipOwnProcess);

        if (_hook == 0)
        {
            FileLog.Write("agent", $"Cannot install foreground window watcher. Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    public void Dispose()
    {
        if (_hook == 0)
        {
            return;
        }

        UnhookWinEvent(_hook);
        _hook = 0;
    }

    private void HandleWinEvent(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (eventType != EventSystemForeground || window == 0)
        {
            return;
        }

        _onForegroundChanged();
    }

    private delegate void WinEventProc(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint module,
        WinEventProc callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hook);
}
