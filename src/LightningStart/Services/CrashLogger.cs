using System.IO;
using System.Reflection;
using System.Text;
using LightningStart.Infrastructure;

namespace LightningStart.Services;

internal static class CrashLogger
{
    private static readonly object Sync = new();

    public static string LogDirectory => AppPaths.LogsDirectory;

    public static string LogFilePath => Path.Combine(LogDirectory, $"crash-{DateTime.Now:yyyyMMdd}.log");

    public static void Write(string context, Exception exception)
    {
        try
        {
            var entry = new StringBuilder()
                .AppendLine("============================================================")
                .Append("Time: ").AppendLine(DateTimeOffset.Now.ToString("O"))
                .Append("Version: ").AppendLine(GetVersion())
                .Append("Context: ").AppendLine(context)
                .AppendLine(exception.ToString())
                .ToString();

            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogFilePath, entry, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // Logging must never throw or mask the original failure.
        }
    }

    private static string GetVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version?.ToString(3) ?? "unknown";
    }
}
