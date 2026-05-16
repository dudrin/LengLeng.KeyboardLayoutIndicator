using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class SessionAgentManager
{
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint StartfUseShowWindow = 0x00000001;
    private const ushort SwHide = 0;

    private readonly string _settingsPath;
    private readonly string _executablePath;
    private readonly string _processName;

    public SessionAgentManager(string settingsPath)
    {
        _settingsPath = settingsPath;
        _executablePath = AppPaths.ExecutablePath;
        _processName = Path.GetFileNameWithoutExtension(_executablePath);
    }

    public void EnsureAgentsForActiveSessions()
    {
        foreach (var session in EnumerateActiveSessions())
        {
            if (session.SessionId <= 0 || IsAgentRunning(session.SessionId))
            {
                continue;
            }

            LaunchAgent(session.SessionId);
        }
    }

    private bool IsAgentRunning(int sessionId)
    {
        try
        {
            foreach (var process in Process.GetProcessesByName(_processName))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId)
                    {
                        continue;
                    }

                    if (process.SessionId == sessionId)
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            FileLog.Write("service", $"Cannot inspect processes for session {sessionId}.", ex);
        }

        return false;
    }

    private void LaunchAgent(int sessionId)
    {
        var commandLine =
            $"{CommandLine.Quote(_executablePath)} --agent --config {CommandLine.Quote(_settingsPath)}";

        nint token = 0;
        nint environment = 0;
        try
        {
            if (!WTSQueryUserToken((uint)sessionId, out token))
            {
                FileLog.Write(
                    "service",
                    $"WTSQueryUserToken failed for session {sessionId}. Win32 error {Marshal.GetLastWin32Error()}.");
                return;
            }

            if (!CreateEnvironmentBlock(out environment, token, false))
            {
                FileLog.Write(
                    "service",
                    $"CreateEnvironmentBlock failed for session {sessionId}. Win32 error {Marshal.GetLastWin32Error()}.");
                environment = 0;
            }

            var startupInfo = new StartupInfo
            {
                Cb = Marshal.SizeOf<StartupInfo>(),
                Desktop = @"winsta0\default",
                Flags = StartfUseShowWindow,
                ShowWindow = SwHide
            };

            if (!CreateProcessAsUser(
                    token,
                    _executablePath,
                    commandLine,
                    0,
                    0,
                    false,
                    CreateNoWindow | CreateUnicodeEnvironment,
                    environment,
                    Path.GetDirectoryName(_executablePath),
                    ref startupInfo,
                    out var processInformation))
            {
                FileLog.Write(
                    "service",
                    $"CreateProcessAsUser failed for session {sessionId}. Win32 error {Marshal.GetLastWin32Error()}.");
                return;
            }

            CloseHandle(processInformation.Process);
            CloseHandle(processInformation.Thread);
            FileLog.Write("service", $"Agent launched in session {sessionId}.");
        }
        finally
        {
            if (environment != 0)
            {
                DestroyEnvironmentBlock(environment);
            }

            if (token != 0)
            {
                CloseHandle(token);
            }
        }
    }

    private static IEnumerable<WtsSession> EnumerateActiveSessions()
    {
        if (!WTSEnumerateSessions(0, 0, 1, out var sessionInfoPointer, out var count))
        {
            FileLog.Write("service", $"WTSEnumerateSessions failed. Win32 error {Marshal.GetLastWin32Error()}.");
            yield break;
        }

        try
        {
            var dataSize = Marshal.SizeOf<WtsSessionInfo>();
            for (var i = 0; i < count; i++)
            {
                var current = sessionInfoPointer + i * dataSize;
                var nativeSession = Marshal.PtrToStructure<WtsSessionInfo>(current);
                if (nativeSession.State == WtsConnectState.Active)
                {
                    yield return new WtsSession(nativeSession.SessionId);
                }
            }
        }
        finally
        {
            WTSFreeMemory(sessionInfoPointer);
        }
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSEnumerateSessions(
        nint server,
        int reserved,
        int version,
        out nint sessionInfo,
        out int count);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(nint memory);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out nint token);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out nint environment, nint token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(nint environment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        nint token,
        string applicationName,
        string commandLine,
        nint processAttributes,
        nint threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        nint environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    private readonly record struct WtsSession(int SessionId);

    private enum WtsConnectState
    {
        Active,
        Connected,
        ConnectQuery,
        Shadow,
        Disconnected,
        Idle,
        Listen,
        Reset,
        Down,
        Init
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        public int SessionId;
        public nint WinStationName;
        public WtsConnectState State;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Cb;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2;
        public nint Reserved2Pointer;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }
}
