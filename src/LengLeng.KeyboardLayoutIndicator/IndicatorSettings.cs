using System.Text.Json.Serialization;

namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class IndicatorSettings
{
    public const int CurrentSchemaVersion = 6;

    [JsonPropertyName("settingsSchemaVersion")]
    public int SettingsSchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("englishScrollLockState")]
    public string EnglishScrollLockState { get; set; } = "Off";

    [JsonPropertyName("indicatorKey")]
    public string IndicatorKey { get; set; } = LockKeyCatalog.CapsLock;

    [JsonPropertyName("englishIndicatorState")]
    public string EnglishIndicatorState { get; set; } = "Preserve";

    [JsonPropertyName("englishLanguagePrefixes")]
    public string[] EnglishLanguagePrefixes { get; set; } = { "en" };

    [JsonPropertyName("blinkIntervalMs")]
    public int BlinkIntervalMs { get; set; } = 140;

    [JsonPropertyName("indicatorOnBlinkLitMs")]
    public int IndicatorOnBlinkLitMs { get; set; } = 260;

    [JsonPropertyName("indicatorOnBlinkDarkMs")]
    public int IndicatorOnBlinkDarkMs { get; set; } = 80;

    [JsonPropertyName("indicatorOffBlinkLitMs")]
    public int IndicatorOffBlinkLitMs { get; set; } = 120;

    [JsonPropertyName("indicatorOffBlinkDarkMs")]
    public int IndicatorOffBlinkDarkMs { get; set; } = 650;

    [JsonPropertyName("layoutPollIntervalMs")]
    public int LayoutPollIntervalMs { get; set; } = 50;

    [JsonPropertyName("serviceSessionPollIntervalMs")]
    public int ServiceSessionPollIntervalMs { get; set; } = 5000;

    [JsonPropertyName("treatUnknownLayoutAsEnglish")]
    public bool TreatUnknownLayoutAsEnglish { get; set; } = true;

    [JsonPropertyName("consoleLayoutStrategy")]
    public string ConsoleLayoutStrategy { get; set; } = "ForegroundThread";

    [JsonPropertyName("layoutDetectionStrategy")]
    public string LayoutDetectionStrategy { get; set; } = "TrayIndicatorFirst";

    [JsonPropertyName("trayIndicatorConsoleProcessNames")]
    public string[] TrayIndicatorConsoleProcessNames { get; set; } = { "Far", "Far64" };

    [JsonPropertyName("pauseIndicatorWhileModifiersDown")]
    public bool PauseIndicatorWhileModifiersDown { get; set; } = true;

    [JsonPropertyName("modifierReleasePauseMs")]
    public int ModifierReleasePauseMs { get; set; } = 250;

    [JsonPropertyName("pauseIndicatorWhileTyping")]
    public bool PauseIndicatorWhileTyping { get; set; } = true;

    [JsonPropertyName("typingPauseMs")]
    public int TypingPauseMs { get; set; } = 700;

    [JsonPropertyName("pauseIndicatorWhileMouseOverTaskbar")]
    public bool PauseIndicatorWhileMouseOverTaskbar { get; set; } = true;

    [JsonPropertyName("taskbarPreviewHoverBandPx")]
    public int TaskbarPreviewHoverBandPx { get; set; } = 280;

    [JsonPropertyName("taskbarHoverReleasePauseMs")]
    public int TaskbarHoverReleasePauseMs { get; set; } = 700;

    [JsonPropertyName("restoreInitialScrollLockStateOnExit")]
    public bool RestoreInitialScrollLockStateOnExit { get; set; } = true;

    [JsonPropertyName("logLayoutChanges")]
    public bool LogLayoutChanges { get; set; } = true;

    [JsonPropertyName("manualEnglishIndicatorRect")]
    public ScreenRectangle? ManualEnglishIndicatorRect { get; set; }

    [JsonPropertyName("manualEnglishIndicatorTemplate")]
    public string[]? ManualEnglishIndicatorTemplate { get; set; }

    [JsonIgnore]
    public bool EnglishScrollLockOn =>
        string.Equals(EnglishScrollLockState, "On", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool PreserveEnglishIndicatorState =>
        string.Equals(EnglishIndicatorState, "Preserve", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool EnglishIndicatorOn =>
        string.Equals(EnglishIndicatorState, "On", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public ushort IndicatorVirtualKey => LockKeyCatalog.GetVirtualKey(IndicatorKey);

    [JsonIgnore]
    public string IndicatorDisplayName => LockKeyCatalog.GetDisplayName(IndicatorKey);

    public void Validate()
    {
        if (SettingsSchemaVersion <= 0)
        {
            SettingsSchemaVersion = CurrentSchemaVersion;
        }

        if (!string.Equals(EnglishScrollLockState, "On", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(EnglishScrollLockState, "Off", StringComparison.OrdinalIgnoreCase))
        {
            EnglishScrollLockState = "Off";
        }

        IndicatorKey = LockKeyCatalog.Normalize(IndicatorKey);

        if (!string.Equals(EnglishIndicatorState, "Preserve", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(EnglishIndicatorState, "On", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(EnglishIndicatorState, "Off", StringComparison.OrdinalIgnoreCase))
        {
            EnglishIndicatorState = "Preserve";
        }

        if (EnglishLanguagePrefixes.Length == 0)
        {
            EnglishLanguagePrefixes = new[] { "en" };
        }

        EnglishLanguagePrefixes = EnglishLanguagePrefixes
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(prefix => prefix.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (EnglishLanguagePrefixes.Length == 0)
        {
            EnglishLanguagePrefixes = new[] { "en" };
        }

        BlinkIntervalMs = Math.Clamp(BlinkIntervalMs, 80, 1000);
        IndicatorOnBlinkLitMs = Math.Clamp(IndicatorOnBlinkLitMs, 80, 2000);
        IndicatorOnBlinkDarkMs = Math.Clamp(IndicatorOnBlinkDarkMs, 40, 2000);
        IndicatorOffBlinkLitMs = Math.Clamp(IndicatorOffBlinkLitMs, 40, 2000);
        IndicatorOffBlinkDarkMs = Math.Clamp(IndicatorOffBlinkDarkMs, 80, 3000);
        LayoutPollIntervalMs = Math.Clamp(LayoutPollIntervalMs, 50, 2000);
        ServiceSessionPollIntervalMs = Math.Clamp(ServiceSessionPollIntervalMs, 1000, 60000);
        ModifierReleasePauseMs = Math.Clamp(ModifierReleasePauseMs, 0, 2000);
        TypingPauseMs = Math.Clamp(TypingPauseMs, 100, 5000);
        TaskbarPreviewHoverBandPx = Math.Clamp(TaskbarPreviewHoverBandPx, 0, 800);
        TaskbarHoverReleasePauseMs = Math.Clamp(TaskbarHoverReleasePauseMs, 0, 5000);

        if (!string.Equals(ConsoleLayoutStrategy, "ForegroundThread", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ConsoleLayoutStrategy, "PreferNonEnglishProcessThread", StringComparison.OrdinalIgnoreCase))
        {
            ConsoleLayoutStrategy = "ForegroundThread";
        }

        if (!string.Equals(LayoutDetectionStrategy, "ForegroundWindow", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(LayoutDetectionStrategy, "TrayIndicatorForConsole", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(LayoutDetectionStrategy, "TrayIndicatorFirst", StringComparison.OrdinalIgnoreCase))
        {
            LayoutDetectionStrategy = "TrayIndicatorFirst";
        }

        TrayIndicatorConsoleProcessNames = TrayIndicatorConsoleProcessNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => Path.GetFileNameWithoutExtension(name.Trim()))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (TrayIndicatorConsoleProcessNames.Length == 0)
        {
            TrayIndicatorConsoleProcessNames = new[] { "Far", "Far64" };
        }

        if (ManualEnglishIndicatorRect is { IsValid: false })
        {
            ManualEnglishIndicatorRect = null;
        }

        if (ManualEnglishIndicatorTemplate is not null
            && ManualEnglishIndicatorTemplate.Length != TrayInputIndicatorReader.NormalizedHeight)
        {
            ManualEnglishIndicatorTemplate = null;
        }
    }

    [JsonIgnore]
    public bool PreferNonEnglishConsoleProcessThread =>
        string.Equals(
            ConsoleLayoutStrategy,
            "PreferNonEnglishProcessThread",
            StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool UseSystemInputMethodFirst =>
        string.Equals(
            LayoutDetectionStrategy,
            "SystemInputMethodFirst",
            StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool UseTrayIndicatorFirst =>
        string.Equals(
            LayoutDetectionStrategy,
            "TrayIndicatorFirst",
            StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool UseTrayIndicatorForConsole =>
        string.Equals(
            LayoutDetectionStrategy,
            "TrayIndicatorForConsole",
            StringComparison.OrdinalIgnoreCase);

    public bool ShouldUseTrayIndicatorForConsoleProcess(string processName)
    {
        return TrayIndicatorConsoleProcessNames.Any(name =>
            string.Equals(name, processName, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsEnglish(LayoutSnapshot layout)
    {
        if (!layout.IsKnown)
        {
            return TreatUnknownLayoutAsEnglish;
        }

        return EnglishLanguagePrefixes.Any(prefix =>
            MatchesPrefix(layout.CultureName, prefix)
            || MatchesPrefix(layout.TwoLetterLanguageName, prefix));
    }

    private static bool MatchesPrefix(string value, string prefix)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(prefix))
        {
            return false;
        }

        return value.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || value.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase);
    }
}
