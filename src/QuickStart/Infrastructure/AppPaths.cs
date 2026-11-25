using System.IO;

namespace QuickStart.Infrastructure;

internal static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickStart");

    public static string ConfigPath { get; } = Path.Combine(DataDirectory, "config.json");

    public static string IconCacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickStart", "icons");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(IconCacheDirectory);
    }
}
