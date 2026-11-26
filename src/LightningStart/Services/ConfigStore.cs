using System.IO;
using System.Text;
using System.Text.Json;
using LightningStart.Infrastructure;
using LightningStart.Models;

namespace LightningStart.Services;

internal sealed class ConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public string ConfigPath => AppPaths.ConfigPath;

    public bool Exists => File.Exists(ConfigPath);

    public AppConfig Load()
    {
        if (!Exists)
            return CreateDefault();

        try
        {
            var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
            var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
            if (config is null)
                throw new JsonException("Configuration root is null.");

            Normalize(config);
            return config;
        }
        catch
        {
            // Keep the broken file for inspection and start from a clean default.
            try
            {
                File.Copy(ConfigPath, ConfigPath + ".bad", overwrite: true);
            }
            catch
            {
                // Ignore.
            }

            return CreateDefault();
        }
    }

    public void Save(AppConfig config)
    {
        AppPaths.EnsureCreated();
        var json = JsonSerializer.Serialize(config, SerializerOptions);
        var tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tempPath, ConfigPath, overwrite: true);
    }

    private static void Normalize(AppConfig config)
    {
        config.Behavior ??= new BehaviorConfig();
        config.Behavior.Normalize();

        config.Entries ??= new List<AppEntry>();
        foreach (var entry in config.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
                entry.Id = Guid.NewGuid().ToString("N");

            entry.Target = (entry.Target ?? string.Empty).Trim();
            entry.Arguments ??= string.Empty;
            entry.WorkingDirectory ??= string.Empty;

            if (string.IsNullOrWhiteSpace(entry.Name))
                entry.Name = AppNameHelper.Suggest(entry.Target);

            entry.Name = entry.Name.Trim();
        }

        config.Entries.RemoveAll(entry => string.IsNullOrWhiteSpace(entry.Target));
    }

    private static AppConfig CreateDefault()
    {
        var config = new AppConfig();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system32 = Path.Combine(windows, "System32");

        AddIfExists(config, "文件资源管理器", Path.Combine(windows, "explorer.exe"));
        AddIfExists(config, "记事本", Path.Combine(system32, "notepad.exe"));
        AddIfExists(config, "PowerShell", Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell.exe"));

        var windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "wt.exe");
        AddIfExists(config, "Windows 终端", windowsApps);

        return config;
    }

    private static void AddIfExists(AppConfig config, string name, string target)
    {
        if (!File.Exists(target))
            return;

        config.Entries.Add(new AppEntry
        {
            Name = name,
            Target = target
        });
    }
}
