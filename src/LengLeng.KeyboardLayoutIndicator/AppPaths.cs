using System.Diagnostics;

namespace LengLeng.KeyboardLayoutIndicator;

internal static class AppPaths
{
    public static string ProgramDataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LengLeng",
            "KeyboardLayoutIndicator");

    public static string LogDirectory => Path.Combine(ProgramDataDirectory, "logs");

    public static string DefaultSettingsPath => Path.Combine(ProgramDataDirectory, "appsettings.json");

    public static string AgentStopSignalPath => Path.Combine(ProgramDataDirectory, "agent.stop");

    public static string ExecutablePath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                return Environment.ProcessPath;
            }

            using var currentProcess = Process.GetCurrentProcess();
            return currentProcess.MainModule?.FileName
                ?? throw new InvalidOperationException("Cannot resolve executable path.");
        }
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ProgramDataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
