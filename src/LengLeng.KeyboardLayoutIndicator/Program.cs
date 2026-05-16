namespace LengLeng.KeyboardLayoutIndicator;

internal static class Program
{
    public const string ServiceName = "LengLengKeyboardLayoutIndicator";
    public const string DisplayName = "LengLeng Keyboard Layout Indicator";

    public static int Main(string[] args)
    {
        try
        {
            if (CommandLine.HasSwitch(args, "--agent"))
            {
                return AgentApplication.Run(args);
            }

            if (CommandLine.HasSwitch(args, "--diagnostics"))
            {
                return DiagnosticsApplication.Run(args);
            }

            if (CommandLine.HasSwitch(args, "--stop-service")
                || CommandLine.HasSwitch(args, "--start-service"))
            {
                return ServiceCommandApplication.Run(args);
            }

            var configPath = CommandLine.GetOption(args, "--config");

            if (CommandLine.HasSwitch(args, "--run-service-console"))
            {
                return ConsoleServiceApplication.Run(configPath);
            }

            return WindowsServiceHost.Run(new KeyboardLayoutServiceRuntime(configPath));
        }
        catch (Exception ex)
        {
            FileLog.Write("main", "Fatal error.", ex);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
