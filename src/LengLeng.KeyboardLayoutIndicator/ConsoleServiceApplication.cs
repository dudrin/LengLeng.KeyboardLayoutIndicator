namespace LengLeng.KeyboardLayoutIndicator;

internal static class ConsoleServiceApplication
{
    public static int Run(string? configPath)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        Console.WriteLine("Running service loop in console mode. Press Ctrl+C to stop.");
        new KeyboardLayoutServiceRuntime(configPath).Run(cancellation.Token);
        return 0;
    }
}
