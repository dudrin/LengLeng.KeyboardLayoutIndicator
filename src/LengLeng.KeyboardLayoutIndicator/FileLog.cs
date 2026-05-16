using System.Text;

namespace LengLeng.KeyboardLayoutIndicator;

internal static class FileLog
{
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private static readonly object SyncRoot = new();

    public static string LogPath => Path.Combine(AppPaths.LogDirectory, "service.log");

    public static void Write(string component, string message, Exception? exception = null)
    {
        try
        {
            AppPaths.EnsureDirectories();

            lock (SyncRoot)
            {
                RotateIfNeeded();

                var builder = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("O"))
                    .Append(" [")
                    .Append(component)
                    .Append("] ")
                    .Append(message);

                if (exception is not null)
                {
                    builder
                        .AppendLine()
                        .Append(exception);
                }

                builder.AppendLine();
                File.AppendAllText(LogPath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never break the indicator.
        }
    }

    private static void RotateIfNeeded()
    {
        var logFile = new FileInfo(LogPath);
        if (!logFile.Exists || logFile.Length < MaxLogBytes)
        {
            return;
        }

        var oldPath = LogPath + ".old";
        if (File.Exists(oldPath))
        {
            File.Delete(oldPath);
        }

        File.Move(LogPath, oldPath);
    }
}
