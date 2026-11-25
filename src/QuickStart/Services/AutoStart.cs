using Microsoft.Win32;

namespace QuickStart.Services;

internal static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QuickStart";

    public static void Sync(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var command = BuildCommand();
                if (command is null)
                    return;

                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.Ordinal))
                    key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key?.GetValue(ValueName) is not null)
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
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
