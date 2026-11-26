using System.IO;

namespace QuickStart.Infrastructure;

internal static class AppPaths
{
    private const string ProductDirectoryName = "LightningStart";
    private const string LegacyProductDirectoryName = "QuickStart";

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductDirectoryName);

    public static string ConfigPath { get; } = Path.Combine(DataDirectory, "config.json");

    public static string IconCacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductDirectoryName, "icons");

    public static string LogsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductDirectoryName, "logs");

    private static string LegacyConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        LegacyProductDirectoryName,
        "config.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(IconCacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <summary>
    /// Copies the configuration from the pre-rename QuickStart location once.
    /// The old file is left in place as a backup.
    /// </summary>
    public static bool MigrateLegacyConfigIfNeeded()
    {
        try
        {
            if (File.Exists(ConfigPath) || !File.Exists(LegacyConfigPath))
                return false;

            Directory.CreateDirectory(DataDirectory);
            File.Copy(LegacyConfigPath, ConfigPath, overwrite: false);
            return true;
        }
        catch
        {
            // A failed migration is non-fatal: the app will start with defaults.
            return false;
        }
    }
}
