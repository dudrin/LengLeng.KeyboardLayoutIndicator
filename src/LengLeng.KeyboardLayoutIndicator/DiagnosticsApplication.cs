namespace LengLeng.KeyboardLayoutIndicator;

internal static class DiagnosticsApplication
{
    public static int Run(string[] args)
    {
        var configPath = CommandLine.GetOption(args, "--config");
        var settingsPath = SettingsStore.ResolvePath(configPath);
        var settings = SettingsStore.LoadOrCreate(settingsPath);
        var layout = new KeyboardLayoutReader().GetForegroundLayout(settings);
        var trayLayout = new TrayInputIndicatorReader().GetCurrentLayout(settings);
        var indicatorController = new ScrollLockController(settings.IndicatorKey);
        var indicatorOn = indicatorController.GetState();
        var mouseOverTaskbarOrPreview =
            new TaskbarHoverDetector().IsCursorOverTaskbarOrPreview(settings.TaskbarPreviewHoverBandPx);

        Console.WriteLine($"Service name: {Program.ServiceName}");
        Console.WriteLine($"Executable: {AppPaths.ExecutablePath}");
        Console.WriteLine($"Settings: {settingsPath}");
        Console.WriteLine($"English prefixes: {string.Join(", ", settings.EnglishLanguagePrefixes)}");
        Console.WriteLine($"English Scroll Lock state: {settings.EnglishScrollLockState}");
        Console.WriteLine($"Indicator key: {settings.IndicatorDisplayName}");
        Console.WriteLine($"English indicator state: {settings.EnglishIndicatorState}");
        Console.WriteLine($"Blink interval: {settings.BlinkIntervalMs} ms");
        Console.WriteLine($"Indicator on blink: {settings.IndicatorOnBlinkLitMs}/{settings.IndicatorOnBlinkDarkMs} ms");
        Console.WriteLine($"Indicator off blink: {settings.IndicatorOffBlinkLitMs}/{settings.IndicatorOffBlinkDarkMs} ms");
        Console.WriteLine($"Layout detection strategy: {settings.LayoutDetectionStrategy}");
        Console.WriteLine($"Pause while modifiers down: {settings.PauseIndicatorWhileModifiersDown}");
        Console.WriteLine($"Pause while typing: {settings.PauseIndicatorWhileTyping}");
        Console.WriteLine($"Typing pause: {settings.TypingPauseMs} ms");
        Console.WriteLine($"Pause while mouse over taskbar: {settings.PauseIndicatorWhileMouseOverTaskbar}");
        Console.WriteLine($"Taskbar preview hover band: {settings.TaskbarPreviewHoverBandPx} px");
        Console.WriteLine($"Taskbar hover release pause: {settings.TaskbarHoverReleasePauseMs} ms");
        Console.WriteLine($"Mouse over taskbar or previews: {mouseOverTaskbarOrPreview}");
        Console.WriteLine($"Current layout: {layout.DisplayName}");
        Console.WriteLine($"Current layout is English: {settings.IsEnglish(layout)}");
        Console.WriteLine($"Tray indicator layout: {trayLayout.DisplayName}");
        Console.WriteLine($"Tray indicator is English: {settings.IsEnglish(trayLayout)}");
        Console.WriteLine($"Current {settings.IndicatorDisplayName}: {(indicatorOn ? "On" : "Off")}");
        Console.WriteLine($"Log: {FileLog.LogPath}");
        return 0;
    }
}
