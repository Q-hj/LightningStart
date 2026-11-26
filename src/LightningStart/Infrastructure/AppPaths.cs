using System.IO;

namespace LightningStart.Infrastructure;

internal static class AppPaths
{
    private const string ProductDirectoryName = "LightningStart";

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductDirectoryName);

    public static string ConfigPath { get; } = Path.Combine(DataDirectory, "config.json");

    public static string IconCacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductDirectoryName, "icons");

    public static string LogsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductDirectoryName, "logs");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(IconCacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
