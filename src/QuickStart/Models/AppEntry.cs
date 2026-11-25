using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media;
using QuickStart.Infrastructure;

namespace QuickStart.Models;

public sealed class AppEntry : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _target = string.Empty;
    private string _arguments = string.Empty;
    private string _workingDirectory = string.Empty;
    private bool _runAsAdmin;
    private string? _iconPath;
    private ImageSource? _icon;
    private bool _isTargetMissing;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>Executable, shortcut, folder, URL or shell parsing name.</summary>
    public string Target
    {
        get => _target;
        set => SetProperty(ref _target, value);
    }

    public string Arguments
    {
        get => _arguments;
        set => SetProperty(ref _arguments, value);
    }

    public string WorkingDirectory
    {
        get => _workingDirectory;
        set => SetProperty(ref _workingDirectory, value);
    }

    public bool RunAsAdmin
    {
        get => _runAsAdmin;
        set => SetProperty(ref _runAsAdmin, value);
    }

    /// <summary>Optional icon override (image file, exe or shortcut).</summary>
    public string? IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value);
    }

    [JsonIgnore]
    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    [JsonIgnore]
    public bool IsTargetMissing
    {
        get => _isTargetMissing;
        set => SetProperty(ref _isTargetMissing, value);
    }

    public bool TargetExists()
    {
        var target = Target?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(target))
            return false;

        if (target.Contains("://", StringComparison.Ordinal))
            return true;
        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("ms-", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(target);
            return File.Exists(expanded) || Directory.Exists(expanded);
        }
        catch
        {
            return false;
        }
    }
}
