using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace LengLeng.KeyboardLayoutIndicator;

internal static class ServiceCommandApplication
{
    public static int Run(string[] args)
    {
        if (!IsAdministrator())
        {
            return RelaunchElevated(args);
        }

        AppPaths.EnsureDirectories();

        if (CommandLine.HasSwitch(args, "--stop-service"))
        {
            return StopService();
        }

        if (CommandLine.HasSwitch(args, "--start-service"))
        {
            return StartService();
        }

        return 1;
    }

    private static int StopService()
    {
        try
        {
            File.WriteAllText(AppPaths.AgentStopSignalPath, DateTimeOffset.Now.ToString("O"));
        }
        catch (Exception ex)
        {
            FileLog.Write("service-command", "Cannot write agent stop signal.", ex);
        }

        var exitCode = RunSc("stop", Program.ServiceName);
        FileLog.Write("service-command", $"Stop service requested. sc.exe exit code: {exitCode}.");
        return exitCode is 0 or 1062 ? 0 : exitCode;
    }

    private static int StartService()
    {
        try
        {
            if (File.Exists(AppPaths.AgentStopSignalPath))
            {
                File.Delete(AppPaths.AgentStopSignalPath);
            }
        }
        catch (Exception ex)
        {
            FileLog.Write("service-command", "Cannot delete agent stop signal.", ex);
        }

        var exitCode = RunSc("start", Program.ServiceName);
        FileLog.Write("service-command", $"Start service requested. sc.exe exit code: {exitCode}.");
        return exitCode is 0 or 1056 ? 0 : exitCode;
    }

    private static int RelaunchElevated(string[] args)
    {
        try
        {
            var argumentLine = string.Join(" ", args.Select(CommandLine.Quote));
            Process.Start(new ProcessStartInfo(AppPaths.ExecutablePath, argumentLine)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(AppPaths.ExecutablePath)
                    ?? Environment.CurrentDirectory
            });

            return 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            FileLog.Write("service-command", "Elevation was cancelled.");
            return 1223;
        }
    }

    private static int RunSc(string command, string serviceName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("sc.exe", $"{command} {serviceName}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
            {
                FileLog.Write("service-command", "Cannot start sc.exe.");
                return 1;
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(20000);

            if (!string.IsNullOrWhiteSpace(output))
            {
                FileLog.Write("service-command", output.Trim());
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                FileLog.Write("service-command", error.Trim());
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            FileLog.Write("service-command", $"Cannot run sc.exe {command}.", ex);
            return 1;
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
