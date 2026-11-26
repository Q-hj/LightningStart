using Microsoft.Win32;

namespace QuickStart.Services;

internal static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LightningStart";
    private const string LegacyValueName = "QuickStart";

    public static void Sync(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
                return;

            if (enabled)
            {
                var command = BuildCommand();
                if (command is not null &&
                    !string.Equals(key.GetValue(ValueName) as string, command, StringComparison.Ordinal))
                {
                    key.SetValue(ValueName, command, RegistryValueKind.String);
                }
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            // Remove the autostart entry written by the pre-rename executable.
            if (key.GetValue(LegacyValueName) is not null)
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Registry access can fail in locked-down environments; the app still works.
        }
    }

    private static string? BuildCommand()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return null;

        return $"\"{executable}\" --tray";
    }
}
