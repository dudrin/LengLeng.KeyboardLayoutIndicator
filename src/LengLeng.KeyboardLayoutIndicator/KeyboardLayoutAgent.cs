namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class KeyboardLayoutAgent
{
    private const uint VirtualKeyShift = 0x10;
    private const uint VirtualKeyLeftShift = 0xA0;
    private const uint VirtualKeyRightShift = 0xA1;
    private const uint VirtualKeyControl = 0x11;
    private const uint VirtualKeyLeftControl = 0xA2;
    private const uint VirtualKeyRightControl = 0xA3;
    private const uint VirtualKeyMenu = 0x12;
    private const uint VirtualKeyLeftMenu = 0xA4;
    private const uint VirtualKeyRightMenu = 0xA5;
    private const uint VirtualKeySpace = 0x20;

    private readonly string _settingsPath;
    private readonly KeyboardLayoutReader _layoutReader = new();
    private readonly TaskbarHoverDetector _taskbarHoverDetector = new();
    private readonly object _stateSync = new();
    private readonly AutoResetEvent _wakeRequested = new(false);

    private IndicatorSettings _settings;
    private DateTime _settingsLastWriteUtc;
    private volatile bool _layoutRefreshRequested = true;
    private LayoutSnapshot _currentLayout = LayoutSnapshot.Unknown;
    private bool _currentLayoutIsEnglish = true;
    private DateTime _nextFallbackLayoutRefreshUtc = DateTime.MinValue;
    private LayoutSnapshot _lastLoggedLayout = LayoutSnapshot.Unknown;
    private bool? _lastLoggedIsEnglish;
    private ScrollLockController _indicatorController;
    private bool _userIndicatorState;
    private DateTime _suppressIndicatorOutputUntilUtc;
    private bool _physicalIndicatorKeyDown;
    private bool _blinkInitialized;
    private bool _blinkOutputState;
    private DateTime _nextBlinkTransitionUtc;
    private bool _lastBlinkBaseState;
    private DateTime _taskbarHoverSuppressUntilUtc;
    private DateTime _nextKeyboardLayoutRefreshRequestUtc = DateTime.MinValue;

    public KeyboardLayoutAgent(string? settingsPath)
    {
        _settingsPath = SettingsStore.ResolvePath(settingsPath);
        _settings = LoadSettings();
        _indicatorController = new ScrollLockController(_settings.IndicatorKey);
        _userIndicatorState = _indicatorController.GetState();
    }

    public void Run(CancellationToken cancellationToken)
    {
        using var trayIcon = new TrayIconHost(
            _settingsPath,
            BeforePhysicalIndicatorKeyDown,
            AfterPhysicalIndicatorKeyUp,
            BeforePhysicalNonIndicatorKeyDown,
            RequestLayoutRefresh);
        trayIcon.Start();
        trayIcon.SetWatchedVirtualKey(_settings.IndicatorVirtualKey);

        try
        {
            using var cancellationRegistration = cancellationToken.Register(RequestWake);

            while (!cancellationToken.IsCancellationRequested && !AgentStopRequested())
            {
                var now = DateTime.UtcNow;
                if (ReloadSettingsIfChanged())
                {
                    trayIcon.SetWatchedVirtualKey(_settings.IndicatorVirtualKey);
                    RequestLayoutRefresh();
                }

                if (ShouldRefreshLayout(now))
                {
                    RefreshLayout(now);
                }

                var layout = _currentLayout;
                var isEnglish = _currentLayoutIsEnglish;
                var foregroundWindow = ForegroundWindowInspector.GetCurrent();
                var protectedWindowPauseActive = _settings.PauseIndicatorWhileProtectedWindowActive
                    && foregroundWindow.BlocksLowerIntegrityInput;
                UpdatePhysicalIndicatorKeyState(now);

                if (_settings.PauseIndicatorWhileModifiersDown && KeyboardInputGuard.IsModifierDown())
                {
                    SuppressIndicatorOutput(now, _settings.ModifierReleasePauseMs);
                }

                var taskbarHoverPauseActive = IsTaskbarHoverPauseActive(now);
                if (taskbarHoverPauseActive)
                {
                    _blinkInitialized = false;
                    trayIcon.UpdateStatus(
                        layout,
                        isEnglish,
                        _settings.IndicatorKey,
                        GetUserIndicatorState(),
                        _indicatorController.GetState(),
                        foregroundWindow,
                        false,
                        _settings.ShowLayoutOverlayForProtectedWindows,
                        _settings.LayoutOverlayDurationMs);
                    WaitForNextTick(now, isEnglish, taskbarHoverPauseActive, false);
                    continue;
                }

                if (protectedWindowPauseActive)
                {
                    _blinkInitialized = false;
                    trayIcon.UpdateStatus(
                        layout,
                        isEnglish,
                        _settings.IndicatorKey,
                        GetUserIndicatorState(),
                        _indicatorController.GetState(),
                        foregroundWindow,
                        true,
                        _settings.ShowLayoutOverlayForProtectedWindows,
                        _settings.LayoutOverlayDurationMs);
                    WaitForNextTick(now, isEnglish, taskbarHoverPauseActive, true);
                    continue;
                }

                if (now < _suppressIndicatorOutputUntilUtc || _physicalIndicatorKeyDown)
                {
                    RestoreUserIndicatorState();
                    trayIcon.UpdateStatus(
                        layout,
                        isEnglish,
                        _settings.IndicatorKey,
                        GetUserIndicatorState(),
                        _indicatorController.GetState(),
                        foregroundWindow,
                        false,
                        _settings.ShowLayoutOverlayForProtectedWindows,
                        _settings.LayoutOverlayDurationMs);
                    WaitForNextTick(now, isEnglish, taskbarHoverPauseActive, false);
                    continue;
                }

                if (isEnglish)
                {
                    ApplyEnglishIndicatorState();
                }
                else
                {
                    ApplyNonEnglishBlink(now);
                }

                trayIcon.UpdateStatus(
                    layout,
                    isEnglish,
                    _settings.IndicatorKey,
                    GetUserIndicatorState(),
                    _indicatorController.GetState(),
                    foregroundWindow,
                    false,
                    _settings.ShowLayoutOverlayForProtectedWindows,
                    _settings.LayoutOverlayDurationMs);

                WaitForNextTick(now, isEnglish, taskbarHoverPauseActive, false);
            }
        }
        finally
        {
            if (_settings.RestoreInitialScrollLockStateOnExit)
            {
                RestoreUserIndicatorState();
            }
        }
    }

    public void RunSingleIteration()
    {
        var layout = _layoutReader.GetForegroundLayout(_settings);
        var isEnglish = _settings.IsEnglish(layout);
        Console.WriteLine($"Settings: {_settingsPath}");
        Console.WriteLine($"Layout: {layout.DisplayName}");
        Console.WriteLine($"English: {isEnglish}");
        Console.WriteLine($"Indicator: {_settings.IndicatorDisplayName}");
        Console.WriteLine($"Indicator state: {(_indicatorController.GetState() ? "On" : "Off")}");
    }

    private IndicatorSettings LoadSettings()
    {
        var settings = SettingsStore.LoadOrCreate(_settingsPath);
        _settingsLastWriteUtc = File.Exists(_settingsPath)
            ? File.GetLastWriteTimeUtc(_settingsPath)
            : DateTime.MinValue;

        return settings;
    }

    private bool ReloadSettingsIfChanged()
    {
        var lastWriteUtc = File.Exists(_settingsPath)
            ? File.GetLastWriteTimeUtc(_settingsPath)
            : DateTime.MinValue;

        if (lastWriteUtc == _settingsLastWriteUtc)
        {
            return false;
        }

        var oldIndicatorKey = _settings.IndicatorKey;
        _settings = LoadSettings();
        if (!string.Equals(oldIndicatorKey, _settings.IndicatorKey, StringComparison.OrdinalIgnoreCase))
        {
            RestoreUserIndicatorState();
            _indicatorController = new ScrollLockController(_settings.IndicatorKey);
            _userIndicatorState = _indicatorController.GetState();
            _blinkInitialized = false;
        }

        FileLog.Write("agent", $"Settings reloaded from {_settingsPath}.");
        return true;
    }

    private void RequestLayoutRefresh()
    {
        _layoutRefreshRequested = true;
        RequestWake();
    }

    private void RequestWake()
    {
        _wakeRequested.Set();
    }

    private bool ShouldRefreshLayout(DateTime now)
    {
        return _layoutRefreshRequested
            || !_currentLayout.IsKnown
            || now >= _nextFallbackLayoutRefreshUtc;
    }

    private void RefreshLayout(DateTime now)
    {
        var layout = _layoutReader.GetForegroundLayout(_settings);
        var isEnglish = _settings.IsEnglish(layout);

        _currentLayout = layout;
        _currentLayoutIsEnglish = isEnglish;
        _layoutRefreshRequested = false;
        _nextFallbackLayoutRefreshUtc = now.AddMilliseconds(_settings.LayoutFallbackPollIntervalMs);

        LogLayoutChange(layout, isEnglish);
    }

    private void LogLayoutChange(LayoutSnapshot layout, bool isEnglish)
    {
        if (!_settings.LogLayoutChanges
            || (layout.Equals(_lastLoggedLayout) && _lastLoggedIsEnglish == isEnglish))
        {
            return;
        }

        _lastLoggedLayout = layout;
        _lastLoggedIsEnglish = isEnglish;

        var mode = isEnglish
            ? _settings.PreserveEnglishIndicatorState
                ? $"English, {_settings.IndicatorDisplayName} preserved"
                : $"English, {_settings.IndicatorDisplayName} {(_settings.EnglishIndicatorOn ? "On" : "Off")}"
            : $"Non-English, {_settings.IndicatorDisplayName} blinking";

        FileLog.Write("agent", $"Layout changed: {layout.DisplayName}; mode: {mode}.");
    }

    private bool BeforePhysicalIndicatorKeyDown()
    {
        bool userIndicatorState;
        lock (_stateSync)
        {
            _physicalIndicatorKeyDown = true;
            _blinkInitialized = false;
            _userIndicatorState = !_userIndicatorState;
            userIndicatorState = _userIndicatorState;
            _suppressIndicatorOutputUntilUtc = DateTime.UtcNow.AddMilliseconds(600);
        }

        var canSendIndicatorInput = CanSendIndicatorInput();
        if (canSendIndicatorInput)
        {
            _indicatorController.TrySetState(userIndicatorState);
        }

        FileLog.Write(
            "agent",
            $"{_indicatorController.DisplayName} user state changed to {(userIndicatorState ? "On" : "Off")}.");
        RequestLayoutRefresh();
        return canSendIndicatorInput;
    }

    private void AfterPhysicalIndicatorKeyUp()
    {
        bool userIndicatorState;
        lock (_stateSync)
        {
            _physicalIndicatorKeyDown = false;
            userIndicatorState = _userIndicatorState;
            _blinkInitialized = false;
            _suppressIndicatorOutputUntilUtc = DateTime.UtcNow.AddMilliseconds(600);
        }

        if (CanSendIndicatorInput())
        {
            _indicatorController.TrySetState(userIndicatorState);
        }

        RequestLayoutRefresh();
    }

    private void BeforePhysicalNonIndicatorKeyDown(uint virtualKey)
    {
        RequestLayoutRefreshForKeyboardInput(virtualKey);

        if (!_settings.PauseIndicatorWhileTyping)
        {
            return;
        }

        ScrollLockController controller;
        bool userIndicatorState;
        lock (_stateSync)
        {
            _blinkInitialized = false;
            _suppressIndicatorOutputUntilUtc = DateTime.UtcNow.AddMilliseconds(_settings.TypingPauseMs);
            controller = _indicatorController;
            userIndicatorState = _userIndicatorState;
        }

        if (CanSendIndicatorInput())
        {
            controller.TrySetState(userIndicatorState);
        }
    }

    private bool CanSendIndicatorInput()
    {
        return !_settings.PauseIndicatorWhileProtectedWindowActive
            || !ForegroundWindowInspector.GetCurrent().BlocksLowerIntegrityInput;
    }

    private void RequestLayoutRefreshForKeyboardInput(uint virtualKey)
    {
        if (!CouldBeLayoutSwitchKey(virtualKey))
        {
            return;
        }

        var now = DateTime.UtcNow;
        lock (_stateSync)
        {
            if (now < _nextKeyboardLayoutRefreshRequestUtc)
            {
                return;
            }

            _nextKeyboardLayoutRefreshRequestUtc = now.AddMilliseconds(250);
        }

        RequestLayoutRefresh();
        _ = Task.Delay(120).ContinueWith(
            _ => RequestLayoutRefresh(),
            TaskScheduler.Default);
    }

    private static bool CouldBeLayoutSwitchKey(uint virtualKey)
    {
        if (IsShiftKey(virtualKey))
        {
            return KeyboardInputGuard.IsAltDown() || KeyboardInputGuard.IsControlDown();
        }

        if (IsAltKey(virtualKey) || IsControlKey(virtualKey))
        {
            return KeyboardInputGuard.IsShiftDown();
        }

        return virtualKey == VirtualKeySpace && KeyboardInputGuard.IsWindowsKeyDown();
    }

    private static bool IsShiftKey(uint virtualKey)
    {
        return virtualKey == VirtualKeyShift
            || virtualKey == VirtualKeyLeftShift
            || virtualKey == VirtualKeyRightShift;
    }

    private static bool IsAltKey(uint virtualKey)
    {
        return virtualKey == VirtualKeyMenu
            || virtualKey == VirtualKeyLeftMenu
            || virtualKey == VirtualKeyRightMenu;
    }

    private static bool IsControlKey(uint virtualKey)
    {
        return virtualKey == VirtualKeyControl
            || virtualKey == VirtualKeyLeftControl
            || virtualKey == VirtualKeyRightControl;
    }

    private void UpdatePhysicalIndicatorKeyState(DateTime now)
    {
        var keyDown = _indicatorController.IsPhysicalKeyDown();
        lock (_stateSync)
        {
            if (keyDown && !_physicalIndicatorKeyDown)
            {
                _physicalIndicatorKeyDown = true;
                _blinkInitialized = false;
                _suppressIndicatorOutputUntilUtc = now.AddMilliseconds(600);
            }
            else if (!keyDown && _physicalIndicatorKeyDown)
            {
                _physicalIndicatorKeyDown = false;
                _blinkInitialized = false;
                _suppressIndicatorOutputUntilUtc = now.AddMilliseconds(600);
            }
        }
    }

    private void SuppressIndicatorOutput(DateTime now, int milliseconds)
    {
        lock (_stateSync)
        {
            _suppressIndicatorOutputUntilUtc = now.AddMilliseconds(milliseconds);
            _blinkInitialized = false;
        }
    }

    private bool IsTaskbarHoverPauseActive(DateTime now)
    {
        if (!_settings.PauseIndicatorWhileMouseOverTaskbar)
        {
            return false;
        }

        if (_taskbarHoverDetector.IsCursorOverTaskbarOrPreview(_settings.TaskbarPreviewHoverBandPx))
        {
            _taskbarHoverSuppressUntilUtc = now.AddMilliseconds(_settings.TaskbarHoverReleasePauseMs);
            return true;
        }

        return now < _taskbarHoverSuppressUntilUtc;
    }

    private void ApplyEnglishIndicatorState()
    {
        _blinkInitialized = false;
        if (_settings.PreserveEnglishIndicatorState)
        {
            RestoreUserIndicatorState();
            return;
        }

        _indicatorController.TrySetState(_settings.EnglishIndicatorOn);
    }

    private void ApplyNonEnglishBlink(DateTime now)
    {
        var baseState = GetUserIndicatorState();
        if (!_blinkInitialized || baseState != _lastBlinkBaseState)
        {
            _lastBlinkBaseState = baseState;
            _blinkOutputState = baseState;
            _blinkInitialized = true;
            _indicatorController.TrySetState(_blinkOutputState);
            _nextBlinkTransitionUtc = now.AddMilliseconds(GetCurrentBlinkDuration(baseState, _blinkOutputState));
            return;
        }

        if (now < _nextBlinkTransitionUtc)
        {
            return;
        }

        _blinkOutputState = !_blinkOutputState;
        _indicatorController.TrySetState(_blinkOutputState);
        _nextBlinkTransitionUtc = now.AddMilliseconds(GetCurrentBlinkDuration(baseState, _blinkOutputState));
    }

    private int GetCurrentBlinkDuration(bool baseState, bool outputState)
    {
        if (baseState)
        {
            return outputState
                ? _settings.IndicatorOnBlinkLitMs
                : _settings.IndicatorOnBlinkDarkMs;
        }

        return outputState
            ? _settings.IndicatorOffBlinkLitMs
            : _settings.IndicatorOffBlinkDarkMs;
    }

    private void RestoreUserIndicatorState()
    {
        _indicatorController.TrySetState(GetUserIndicatorState());
    }

    private bool GetUserIndicatorState()
    {
        lock (_stateSync)
        {
            return _userIndicatorState;
        }
    }

    private static bool AgentStopRequested()
    {
        return File.Exists(AppPaths.AgentStopSignalPath);
    }

    private void WaitForNextTick(
        DateTime now,
        bool isEnglish,
        bool taskbarHoverPauseActive,
        bool indicatorOutputPaused)
    {
        var waitMs = isEnglish
            ? 250
            : GetNonEnglishWaitMilliseconds(now);

        if (indicatorOutputPaused)
        {
            waitMs = 250;
        }

        if (taskbarHoverPauseActive)
        {
            waitMs = Math.Min(waitMs, 80);
        }

        if (now < _suppressIndicatorOutputUntilUtc)
        {
            waitMs = Math.Min(waitMs, MillisecondsUntil(now, _suppressIndicatorOutputUntilUtc));
        }

        if (now < _nextFallbackLayoutRefreshUtc)
        {
            waitMs = Math.Min(waitMs, MillisecondsUntil(now, _nextFallbackLayoutRefreshUtc));
        }
        else
        {
            waitMs = 0;
        }

        _wakeRequested.WaitOne(Math.Clamp(waitMs, 10, 1000));
    }

    private int GetNonEnglishWaitMilliseconds(DateTime now)
    {
        if (_blinkInitialized && now < _nextBlinkTransitionUtc)
        {
            return MillisecondsUntil(now, _nextBlinkTransitionUtc);
        }

        return Math.Clamp(_settings.LayoutPollIntervalMs, 20, 250);
    }

    private static int MillisecondsUntil(DateTime now, DateTime deadline)
    {
        return Math.Max(0, unchecked((int)Math.Ceiling((deadline - now).TotalMilliseconds)));
    }
}
