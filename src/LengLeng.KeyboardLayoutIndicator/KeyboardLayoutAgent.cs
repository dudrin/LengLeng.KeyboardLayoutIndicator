namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class KeyboardLayoutAgent
{
    private readonly string _settingsPath;
    private readonly KeyboardLayoutReader _layoutReader = new();
    private readonly TaskbarHoverDetector _taskbarHoverDetector = new();
    private readonly object _stateSync = new();

    private IndicatorSettings _settings;
    private DateTime _settingsLastWriteUtc;
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
            BeforePhysicalNonIndicatorKeyDown);
        trayIcon.Start();
        trayIcon.SetWatchedVirtualKey(_settings.IndicatorVirtualKey);

        try
        {
            while (!cancellationToken.IsCancellationRequested && !AgentStopRequested())
            {
                if (ReloadSettingsIfChanged())
                {
                    trayIcon.SetWatchedVirtualKey(_settings.IndicatorVirtualKey);
                }

                var layout = _layoutReader.GetForegroundLayout(_settings);
                var isEnglish = _settings.IsEnglish(layout);
                LogLayoutChange(layout, isEnglish);

                var now = DateTime.UtcNow;
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
                        _indicatorController.GetState());
                    Sleep(_settings.LayoutPollIntervalMs, cancellationToken);
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
                        _indicatorController.GetState());
                    Sleep(_settings.LayoutPollIntervalMs, cancellationToken);
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
                    _indicatorController.GetState());

                Sleep(_settings.LayoutPollIntervalMs, cancellationToken);
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

        _indicatorController.TrySetState(userIndicatorState);
        FileLog.Write(
            "agent",
            $"{_indicatorController.DisplayName} user state changed to {(userIndicatorState ? "On" : "Off")}.");
        return true;
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

        _indicatorController.TrySetState(userIndicatorState);
    }

    private void BeforePhysicalNonIndicatorKeyDown(uint virtualKey)
    {
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

        controller.TrySetState(userIndicatorState);
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

    private static void Sleep(int milliseconds, CancellationToken cancellationToken)
    {
        cancellationToken.WaitHandle.WaitOne(milliseconds);
    }
}
