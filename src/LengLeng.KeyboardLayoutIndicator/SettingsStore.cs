using System.Text.Json;

namespace LengLeng.KeyboardLayoutIndicator;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string ResolvePath(string? configuredPath)
    {
        return string.IsNullOrWhiteSpace(configuredPath)
            ? AppPaths.DefaultSettingsPath
            : Path.GetFullPath(configuredPath);
    }

    public static IndicatorSettings LoadOrCreate(string? configuredPath)
    {
        var path = ResolvePath(configuredPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            var defaultSettings = new IndicatorSettings();
            Save(path, defaultSettings);
            return defaultSettings;
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<IndicatorSettings>(json, JsonOptions)
                ?? new IndicatorSettings();

            var shouldRefresh = ShouldRefreshSettingsFile(json);
            if (IsLegacySettingsFile(json))
            {
                settings.SettingsSchemaVersion = IndicatorSettings.CurrentSchemaVersion;
                settings.LayoutDetectionStrategy = "TrayIndicatorFirst";
                shouldRefresh = true;
            }

            if (settings.SettingsSchemaVersion < IndicatorSettings.CurrentSchemaVersion)
            {
                var previousSchemaVersion = settings.SettingsSchemaVersion;
                settings.SettingsSchemaVersion = IndicatorSettings.CurrentSchemaVersion;

                if (previousSchemaVersion < 4)
                {
                    settings.IndicatorKey = LockKeyCatalog.CapsLock;
                    settings.EnglishIndicatorState = "Preserve";
                }

                if (previousSchemaVersion < 5)
                {
                    settings.PauseIndicatorWhileTyping = true;
                    settings.TypingPauseMs = 700;
                }

                if (previousSchemaVersion < 6)
                {
                    settings.PauseIndicatorWhileMouseOverTaskbar = true;
                    settings.TaskbarPreviewHoverBandPx = 280;
                    settings.TaskbarHoverReleasePauseMs = 700;
                }

                if (string.Equals(
                    settings.LayoutDetectionStrategy,
                    "TrayIndicatorForConsole",
                    StringComparison.OrdinalIgnoreCase))
                {
                    settings.LayoutDetectionStrategy = "TrayIndicatorFirst";
                }

                shouldRefresh = true;
            }

            if (shouldRefresh && settings.LayoutPollIntervalMs == 100)
            {
                settings.LayoutPollIntervalMs = 50;
            }

            settings.Validate();
            if (shouldRefresh)
            {
                Save(path, settings);
            }

            return settings;
        }
        catch (Exception ex)
        {
            FileLog.Write("settings", $"Cannot read settings from {path}. Default settings are used.", ex);
            var fallback = new IndicatorSettings();
            fallback.Validate();
            return fallback;
        }
    }

    public static void Save(string path, IndicatorSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static bool ShouldRefreshSettingsFile(string json)
    {
        return !json.Contains("\"settingsSchemaVersion\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"layoutDetectionStrategy\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"pauseIndicatorWhileModifiersDown\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"modifierReleasePauseMs\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"consoleLayoutStrategy\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"trayIndicatorConsoleProcessNames\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"indicatorKey\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"englishIndicatorState\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"indicatorOnBlinkLitMs\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"indicatorOnBlinkDarkMs\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"indicatorOffBlinkLitMs\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"indicatorOffBlinkDarkMs\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"pauseIndicatorWhileTyping\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"typingPauseMs\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"pauseIndicatorWhileMouseOverTaskbar\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"taskbarPreviewHoverBandPx\"", StringComparison.OrdinalIgnoreCase)
            || !json.Contains("\"taskbarHoverReleasePauseMs\"", StringComparison.OrdinalIgnoreCase)
            || json.Contains("SystemInputMethodFirst", StringComparison.OrdinalIgnoreCase)
            || json.Contains("\"PreferNonEnglishConsoleProcessThread\"", StringComparison.OrdinalIgnoreCase)
            || json.Contains("\"UseSystemInputMethodFirst\"", StringComparison.OrdinalIgnoreCase)
            || json.Contains("\"UseTrayIndicatorFirst\"", StringComparison.OrdinalIgnoreCase)
            || json.Contains("\"UseTrayIndicatorForConsole\"", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacySettingsFile(string json)
    {
        return !json.Contains("\"settingsSchemaVersion\"", StringComparison.OrdinalIgnoreCase);
    }
}
