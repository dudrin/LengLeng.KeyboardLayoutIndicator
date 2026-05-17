using System.Runtime.InteropServices;

namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class ScrollLockController
{
    private const int InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private readonly ushort _virtualKey;
    private DateTime _nextSendInputFailureLogUtc = DateTime.MinValue;

    public ScrollLockController()
        : this(LockKeyCatalog.ScrollLock)
    {
    }

    public ScrollLockController(string indicatorKey)
    {
        IndicatorKey = LockKeyCatalog.Normalize(indicatorKey);
        DisplayName = LockKeyCatalog.GetDisplayName(IndicatorKey);
        _virtualKey = LockKeyCatalog.GetVirtualKey(IndicatorKey);
    }

    public string IndicatorKey { get; }

    public string DisplayName { get; }

    public bool GetState()
    {
        return (GetKeyState(_virtualKey) & 0x0001) != 0;
    }

    public bool IsPhysicalKeyDown()
    {
        return (GetAsyncKeyState(_virtualKey) & 0x8000) != 0;
    }

    public bool TrySetState(bool enabled)
    {
        if (GetState() == enabled)
        {
            return true;
        }

        Toggle();
        Thread.Sleep(15);

        if (GetState() == enabled)
        {
            return true;
        }

        Toggle();
        Thread.Sleep(15);
        return GetState() == enabled;
    }

    public bool Toggle()
    {
        var inputs = new[]
        {
            new Input
            {
                Type = InputKeyboard,
                Union = new InputUnion
                {
                    KeyboardInput = new KeyboardInput
                    {
                        VirtualKey = _virtualKey
                    }
                }
            },
            new Input
            {
                Type = InputKeyboard,
                Union = new InputUnion
                {
                    KeyboardInput = new KeyboardInput
                    {
                        VirtualKey = _virtualKey,
                        Flags = KeyEventKeyUp
                    }
                }
            }
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent == inputs.Length)
        {
            return true;
        }

        var now = DateTime.UtcNow;
        if (now >= _nextSendInputFailureLogUtc)
        {
            _nextSendInputFailureLogUtc = now.AddSeconds(5);
            FileLog.Write(
                "agent",
                $"{DisplayName} SendInput failed. Sent {sent}/{inputs.Length}. Win32 error {Marshal.GetLastWin32Error()}.");
        }

        return false;
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput MouseInput;

        [FieldOffset(0)]
        public KeyboardInput KeyboardInput;

        [FieldOffset(0)]
        public HardwareInput HardwareInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }
}
