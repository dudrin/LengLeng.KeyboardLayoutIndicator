namespace LengLeng.KeyboardLayoutIndicator;

internal sealed class KeyboardLayoutServiceRuntime : IServiceRuntime
{
    private readonly string _settingsPath;
    private readonly AutoResetEvent _scanRequested = new(false);
    private readonly SessionAgentManager _agentManager;

    public KeyboardLayoutServiceRuntime(string? settingsPath)
    {
        _settingsPath = SettingsStore.ResolvePath(settingsPath);
        _agentManager = new SessionAgentManager(_settingsPath);
    }

    public void Run(CancellationToken cancellationToken)
    {
        AppPaths.EnsureDirectories();
        SettingsStore.LoadOrCreate(_settingsPath);
        TryDeleteStopSignal();

        FileLog.Write("service", $"Service started. Settings: {_settingsPath}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var settings = SettingsStore.LoadOrCreate(_settingsPath);
                _agentManager.EnsureAgentsForActiveSessions();

                WaitHandle.WaitAny(
                    new[] { _scanRequested, cancellationToken.WaitHandle },
                    settings.ServiceSessionPollIntervalMs);
            }
        }
        finally
        {
            SignalAgentsToStop();
            FileLog.Write("service", "Service stopped.");
        }
    }

    public void RequestScan()
    {
        _scanRequested.Set();
    }

    private static void TryDeleteStopSignal()
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
            FileLog.Write("service", "Cannot delete stale agent stop signal.", ex);
        }
    }

    private static void SignalAgentsToStop()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
            File.WriteAllText(AppPaths.AgentStopSignalPath, DateTimeOffset.Now.ToString("O"));
        }
        catch (Exception ex)
        {
            FileLog.Write("service", "Cannot write agent stop signal.", ex);
        }

        Thread.Sleep(1500);
    }
}
