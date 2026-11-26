using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using LightningStart.Models;

namespace LightningStart.Services;

internal static class AppLauncher
{
    private static readonly ConcurrentDictionary<string, long> LastLaunchTicks = new();

    public static bool TryLaunch(AppEntry entry, out string? error)
    {
        error = null;

        var now = Environment.TickCount64;
        if (LastLaunchTicks.TryGetValue(entry.Id, out var last) && now - last < 450)
            return true;
        LastLaunchTicks[entry.Id] = now;

        try
        {
            var target = Environment.ExpandEnvironmentVariables(entry.Target.Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(target))
            {
                error = "启动目标为空";
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = target
            };

            if (!string.IsNullOrWhiteSpace(entry.Arguments))
                startInfo.Arguments = Environment.ExpandEnvironmentVariables(entry.Arguments);

            if (!string.IsNullOrWhiteSpace(entry.WorkingDirectory))
            {
                var workingDirectory = Environment.ExpandEnvironmentVariables(entry.WorkingDirectory.Trim());
                if (Directory.Exists(workingDirectory))
                    startInfo.WorkingDirectory = workingDirectory;
            }

            if (entry.RunAsAdmin)
                startInfo.Verb = "runas";

            // shell:AppsFolder\AUMID style targets must be handed to Explorer.
            if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.FileName = "explorer.exe";
                startInfo.Arguments = target.Contains(' ')
                    ? $"\"{target}\""
                    : target;
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user dismissed the UAC prompt; this is not an error worth surfacing.
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
