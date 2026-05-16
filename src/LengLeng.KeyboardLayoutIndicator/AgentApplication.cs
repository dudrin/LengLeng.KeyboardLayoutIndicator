using System.Diagnostics;

namespace LengLeng.KeyboardLayoutIndicator;

internal static class AgentApplication
{
    public static int Run(string[] args)
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        using var mutex = new Mutex(false, $@"Local\LengLeng.KeyboardLayoutIndicator.Agent.{sessionId}", out var createdNew);
        if (!createdNew)
        {
            return 0;
        }

        var configPath = CommandLine.GetOption(args, "--config");
        var once = CommandLine.HasSwitch(args, "--once");

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        EventHandler processExitHandler = (_, _) =>
        {
            try
            {
                shutdown.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        };

        AppDomain.CurrentDomain.ProcessExit += processExitHandler;

        try
        {
            var agent = new KeyboardLayoutAgent(configPath);

            if (once)
            {
                agent.RunSingleIteration();
                return 0;
            }

            FileLog.Write("agent", $"Agent started in session {sessionId}.");
            agent.Run(shutdown.Token);
            FileLog.Write("agent", $"Agent stopped in session {sessionId}.");
            return 0;
        }
        finally
        {
            AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
        }
    }
}
