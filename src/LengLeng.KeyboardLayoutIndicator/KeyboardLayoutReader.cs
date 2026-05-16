using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class KeyboardLayoutReader
{
    private const int MaxClassNameLength = 256;
    private const string ConsoleWindowClassName = "ConsoleWindowClass";
    private static readonly object ConsoleInspectionFailureSync = new();
    private static readonly HashSet<uint> LoggedConsoleInspectionFailures = new();
    private readonly SystemInputMethodLanguageReader _systemInputMethodLanguageReader = new();
    private readonly TrayInputIndicatorReader _trayInputIndicatorReader = new();

    public LayoutSnapshot GetForegroundLayout(IndicatorSettings? settings = null)
    {
        if (settings?.UseTrayIndicatorFirst == true)
        {
            var trayLayout = _trayInputIndicatorReader.GetCurrentLayout(settings);
            if (trayLayout.IsKnown)
            {
                return trayLayout;
            }
        }

        if (settings?.UseSystemInputMethodFirst == true)
        {
            var systemInputMethodLayout = _systemInputMethodLanguageReader.GetCurrentLayout();
            if (systemInputMethodLayout.IsKnown)
            {
                return systemInputMethodLayout;
            }
        }

        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == 0)
        {
            return LayoutSnapshot.Unknown;
        }

        var threadId = GetWindowThreadProcessId(foregroundWindow, out var processId);
        if (threadId == 0)
        {
            return LayoutSnapshot.Unknown;
        }

        var keyboardLayout = GetKeyboardLayout(threadId);
        var primaryLayout = FromKeyboardLayout(keyboardLayout);
        var isConsoleWindow = IsConsoleWindow(foregroundWindow);

        if (isConsoleWindow
            && settings?.UseTrayIndicatorForConsole == true
            && ShouldUseTrayIndicatorForConsoleProcess(processId, settings))
        {
            var trayLayout = _trayInputIndicatorReader.GetCurrentLayout(settings);
            if (trayLayout.IsKnown)
            {
                return trayLayout;
            }
        }

        if (primaryLayout.IsKnown && !isConsoleWindow)
        {
            return primaryLayout;
        }

        if (isConsoleWindow && settings?.PreferNonEnglishConsoleProcessThread == true)
        {
            var consoleLayout = TryGetConsoleProcessLayout(processId, settings);
            if (consoleLayout.IsKnown)
            {
                return consoleLayout;
            }
        }

        return primaryLayout;
    }

    private static bool ShouldUseTrayIndicatorForConsoleProcess(uint processId, IndicatorSettings settings)
    {
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            return settings.ShouldUseTrayIndicatorForConsoleProcess(process.ProcessName);
        }
        catch (Exception ex)
        {
            FileLog.Write("agent", $"Cannot inspect console process {processId} name.", ex);
            return false;
        }
    }

    private static LayoutSnapshot TryGetConsoleProcessLayout(uint processId, IndicatorSettings? settings)
    {
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            var layouts = process.Threads
                .Cast<ProcessThread>()
                .Select(thread => FromKeyboardLayout(GetKeyboardLayout(unchecked((uint)thread.Id))))
                .Where(layout => layout.IsKnown)
                .GroupBy(layout => layout.LanguageId)
                .Select(group => group.First())
                .ToArray();

            if (layouts.Length == 0)
            {
                return LayoutSnapshot.Unknown;
            }

            var nonEnglishLayout = settings is null
                ? layouts.FirstOrDefault(layout =>
                    !string.Equals(layout.TwoLetterLanguageName, "en", StringComparison.OrdinalIgnoreCase))
                : layouts.FirstOrDefault(layout => !settings.IsEnglish(layout));

            return nonEnglishLayout.IsKnown ? nonEnglishLayout : layouts[0];
        }
        catch (Exception ex)
        {
            lock (ConsoleInspectionFailureSync)
            {
                if (LoggedConsoleInspectionFailures.Add(processId))
                {
                    FileLog.Write("agent", $"Cannot inspect console process {processId} thread layouts.", ex);
                }
            }

            return LayoutSnapshot.Unknown;
        }
    }

    private static LayoutSnapshot FromKeyboardLayout(nint keyboardLayout)
    {
        if (keyboardLayout == 0)
        {
            return LayoutSnapshot.Unknown;
        }

        var languageId = unchecked((int)((long)keyboardLayout & 0xFFFF));
        try
        {
            var culture = new CultureInfo(languageId);
            return new LayoutSnapshot(
                true,
                culture.Name,
                culture.TwoLetterISOLanguageName,
                languageId,
                keyboardLayout);
        }
        catch (CultureNotFoundException)
        {
            return new LayoutSnapshot(
                true,
                $"0x{languageId:X4}",
                $"0x{languageId:X4}",
                languageId,
                keyboardLayout);
        }
    }

    private static bool IsConsoleWindow(nint windowHandle)
    {
        var className = new StringBuilder(MaxClassNameLength);
        if (GetClassName(windowHandle, className, className.Capacity) == 0)
        {
            return false;
        }

        return string.Equals(className.ToString(), ConsoleWindowClassName, StringComparison.Ordinal);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder className, int maxCount);
}
