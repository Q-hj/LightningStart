using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using QuickStart.Infrastructure;
using QuickStart.Models;
using QuickStart.Services;

namespace QuickStart.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ConfigStore _configStore;
    private readonly IconLoader _iconLoader;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _statusTimer;
    private readonly HashSet<AppEntry> _dirtyIcons = new();

    private AppEntry? _selectedEntry;
    private string _statusText = string.Empty;
    private bool _statusIsError;
    private bool _disposed;

    public MainViewModel()
    {
        AppPaths.EnsureCreated();
        AppPaths.MigrateLegacyConfigIfNeeded();

        _configStore = new ConfigStore();
        _iconLoader = new IconLoader();
        IsFirstRun = !_configStore.Exists;

        var config = _configStore.Load();
        Behavior = config.Behavior;
        Behavior.PropertyChanged += OnBehaviorChanged;

        foreach (var entry in config.Entries)
        {
            Attach(entry);
            Entries.Add(entry);
        }

        PanelEntries = new ListCollectionView(Entries)
        {
            Filter = item => item is AppEntry entry && !entry.Hidden
        };
        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(PanelEntryCount));

        AddCommand = new RelayCommand(AddFromDialog);
        RemoveCommand = new RelayCommand(RemoveSelected, () => SelectedEntry is not null);
        MoveUpCommand = new RelayCommand(
            () => MoveSelected(-1),
            () => SelectedEntry is not null && Entries.IndexOf(SelectedEntry) > 0);
        MoveDownCommand = new RelayCommand(
            () => MoveSelected(1),
            () => SelectedEntry is not null && Entries.IndexOf(SelectedEntry) < Entries.Count - 1);
        TestLaunchCommand = new RelayCommand(TestLaunch, () => SelectedEntry is not null);
        OpenTargetFolderCommand = new RelayCommand(OpenTargetFolder, () => SelectedEntry is not null);
        BrowseTargetCommand = new RelayCommand(BrowseTarget, () => SelectedEntry is not null);
        BrowseWorkingDirectoryCommand = new RelayCommand(BrowseWorkingDirectory, () => SelectedEntry is not null);
        BrowseIconCommand = new RelayCommand(BrowseIcon, () => SelectedEntry is not null);
        OpenConfigFolderCommand = new RelayCommand(OpenConfigFolder);
        ToggleHiddenCommand = new RelayCommand<AppEntry>(ToggleHidden);
        DeleteEntryCommand = new RelayCommand<AppEntry>(DeleteEntry);

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
            RefreshDirtyIcons();
        };

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusText = string.Empty;
        };

        SelectedEntry = Entries.FirstOrDefault();
        foreach (var entry in Entries)
            _ = LoadIconAsync(entry);

        AutoStart.Sync(Behavior.StartWithWindows);

        if (IsFirstRun)
            SaveNow();
    }

    public ObservableCollection<AppEntry> Entries { get; } = new();

    /// <summary>Filtered view used by the tray panel; hidden entries are excluded.</summary>
    public ListCollectionView PanelEntries { get; }

    public int PanelEntryCount => PanelEntries.Count;

    public BehaviorConfig Behavior { get; }

    public bool IsFirstRun { get; }

    public string ConfigPath => AppPaths.ConfigPath;

    public string VersionText { get; } =
        $"闪电启动 {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0"}";

    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand TestLaunchCommand { get; }
    public RelayCommand OpenTargetFolderCommand { get; }
    public RelayCommand BrowseTargetCommand { get; }
    public RelayCommand BrowseWorkingDirectoryCommand { get; }
    public RelayCommand BrowseIconCommand { get; }
    public RelayCommand OpenConfigFolderCommand { get; }
    public RelayCommand<AppEntry> ToggleHiddenCommand { get; }
    public RelayCommand<AppEntry> DeleteEntryCommand { get; }

    public AppEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetProperty(ref _selectedEntry, value))
                return;

            OnPropertyChanged(nameof(HasSelection));
            UpdateCommandStates();
        }
    }

    public bool HasSelection => SelectedEntry is not null;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        var added = 0;
        var skipped = 0;
        AppEntry? lastAdded = null;
        var existingTargets = new HashSet<string>(Entries.Select(entry => entry.Target), StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in paths)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                continue;

            var path = rawPath.Trim().Trim('"');
            if (path.Length == 0)
                continue;

            var isSpecial = path.Contains("://", StringComparison.Ordinal)
                || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("ms-", StringComparison.OrdinalIgnoreCase);

            if (!isSpecial && !File.Exists(path) && !Directory.Exists(path))
                continue;

            if (!existingTargets.Add(path))
            {
                skipped++;
                continue;
            }

            var entry = new AppEntry
            {
                Name = AppNameHelper.Suggest(path),
                Target = path
            };

            Attach(entry);
            Entries.Add(entry);
            lastAdded = entry;
            added++;
            _ = LoadIconAsync(entry);
        }

        if (lastAdded is not null)
        {
            SelectedEntry = lastAdded;
            SaveNow();
            var suffix = skipped > 0 ? $"，跳过 {skipped} 个重复项" : string.Empty;
            SetStatus($"已添加 {added} 个程序{suffix}");
        }
        else if (skipped > 0)
        {
            SetStatus("选中的程序已在列表中");
        }
        else
        {
            SetStatus("没有找到可添加的程序", isError: true);
        }
    }

    private void AddFromDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要添加的程序",
            Filter = "程序与快捷方式|*.exe;*.lnk;*.bat;*.cmd;*.url;*.msc|所有文件|*.*",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
            AddFiles(dialog.FileNames);
    }

    private void RemoveSelected()
    {
        if (SelectedEntry is { } entry)
            RemoveEntry(entry);
    }

    private void RemoveEntry(AppEntry entry)
    {
        var index = Entries.IndexOf(entry);
        if (index < 0)
            return;

        var wasSelected = ReferenceEquals(SelectedEntry, entry);
        Detach(entry);
        Entries.Remove(entry);

        if (wasSelected)
        {
            SelectedEntry = Entries.Count == 0
                ? null
                : Entries[Math.Min(index, Entries.Count - 1)];
        }

        UpdateCommandStates();
        SaveNow();
        SetStatus($"已移除 {entry.Name}");
    }

    private void ToggleHidden(AppEntry? entry)
    {
        if (entry is null)
            return;

        entry.Hidden = !entry.Hidden;
        SaveNow();

        SetStatus(entry.Hidden
            ? $"已隐藏 {entry.Name}（托盘面板中不再显示）"
            : $"已显示 {entry.Name}");
    }

    private void DeleteEntry(AppEntry? entry)
    {
        if (entry is not null)
            RemoveEntry(entry);
    }

    public void MoveEntryTo(AppEntry entry, int targetIndex)
    {
        var oldIndex = Entries.IndexOf(entry);
        if (oldIndex < 0)
            return;

        if (targetIndex > oldIndex)
            targetIndex--;

        targetIndex = Math.Clamp(targetIndex, 0, Entries.Count - 1);
        if (targetIndex == oldIndex)
            return;

        Entries.Move(oldIndex, targetIndex);
        SelectedEntry = entry;
        UpdateCommandStates();
        SaveNow();
    }

    private void MoveSelected(int delta)
    {
        if (SelectedEntry is not { } entry)
            return;

        var index = Entries.IndexOf(entry);
        var targetIndex = index + delta;
        if (index < 0 || targetIndex < 0 || targetIndex >= Entries.Count)
            return;

        Entries.Move(index, targetIndex);
        UpdateCommandStates();
        SaveNow();
    }

    private void TestLaunch()
    {
        if (SelectedEntry is not { } entry)
            return;

        if (AppLauncher.TryLaunch(entry, out var error))
            SetStatus($"已启动 {entry.Name}");
        else if (!string.IsNullOrEmpty(error))
            SetStatus("启动失败：" + error, isError: true);
    }

    private void OpenTargetFolder()
    {
        if (SelectedEntry is not { } entry)
            return;

        try
        {
            var target = Environment.ExpandEnvironmentVariables(entry.Target.Trim());
            if (Directory.Exists(target))
            {
                Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            }
            else if (File.Exists(target))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{target}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                SetStatus("目标路径不存在", isError: true);
            }
        }
        catch (Exception ex)
        {
            SetStatus("打开位置失败：" + ex.Message, isError: true);
        }
    }

    private void BrowseTarget()
    {
        if (SelectedEntry is not { } entry)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "选择程序",
            Filter = "程序与快捷方式|*.exe;*.lnk;*.bat;*.cmd;*.url;*.msc|所有文件|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
            entry.Target = dialog.FileName;
    }

    private void BrowseWorkingDirectory()
    {
        if (SelectedEntry is not { } entry)
            return;

        var dialog = new OpenFolderDialog { Title = "选择工作目录" };
        if (dialog.ShowDialog() == true)
            entry.WorkingDirectory = dialog.FolderName;
    }

    private void BrowseIcon()
    {
        if (SelectedEntry is not { } entry)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "选择图标",
            Filter = "图标或程序|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe;*.lnk|所有文件|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
            entry.IconPath = dialog.FileName;
    }

    private void OpenConfigFolder()
    {
        try
        {
            AppPaths.EnsureCreated();
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.DataDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetStatus("打开配置目录失败：" + ex.Message, isError: true);
        }
    }

    private void Attach(AppEntry entry)
    {
        entry.PropertyChanged += OnEntryChanged;
        entry.IsTargetMissing = !entry.TargetExists();
    }

    private void Detach(AppEntry entry)
    {
        entry.PropertyChanged -= OnEntryChanged;
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AppEntry entry)
            return;

        if (e.PropertyName is nameof(AppEntry.Icon) or nameof(AppEntry.IsTargetMissing))
            return;

        if (e.PropertyName is nameof(AppEntry.Target) or nameof(AppEntry.IconPath))
        {
            entry.IsTargetMissing = !entry.TargetExists();
            _dirtyIcons.Add(entry);
        }

        if (e.PropertyName == nameof(AppEntry.Hidden))
        {
            PanelEntries.Refresh();
            OnPropertyChanged(nameof(PanelEntryCount));
        }

        SaveDebounced();
    }

    private void OnBehaviorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BehaviorConfig.StartWithWindows))
        {
            AutoStart.Sync(Behavior.StartWithWindows);
            SetStatus(Behavior.StartWithWindows ? "已启用开机自动启动" : "已关闭开机自动启动");
        }

        SaveDebounced();
    }

    private async Task LoadIconAsync(AppEntry entry)
    {
        var icon = await _iconLoader.LoadAsync(entry);
        if (_disposed || !Entries.Contains(entry))
            return;

        entry.Icon = icon;
    }

    private void RefreshDirtyIcons()
    {
        if (_dirtyIcons.Count == 0)
            return;

        var entries = _dirtyIcons.ToArray();
        _dirtyIcons.Clear();

        foreach (var entry in entries)
        {
            if (Entries.Contains(entry))
                _ = LoadIconAsync(entry);
        }
    }

    private void SaveDebounced()
    {
        if (_disposed)
            return;

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        try
        {
            var config = new AppConfig
            {
                SchemaVersion = 1,
                Behavior = Behavior,
                Entries = Entries.ToList()
            };

            _configStore.Save(config);
        }
        catch (Exception ex)
        {
            SetStatus("保存配置失败：" + ex.Message, isError: true);
        }
    }

    private void SetStatus(string text, bool isError = false)
    {
        StatusText = text;
        StatusIsError = isError;
        _statusTimer?.Stop();
        _statusTimer?.Start();
    }

    private void UpdateCommandStates()
    {
        RemoveCommand.RaiseCanExecuteChanged();
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        TestLaunchCommand.RaiseCanExecuteChanged();
        OpenTargetFolderCommand.RaiseCanExecuteChanged();
        BrowseTargetCommand.RaiseCanExecuteChanged();
        BrowseWorkingDirectoryCommand.RaiseCanExecuteChanged();
        BrowseIconCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _saveTimer.Stop();
        _statusTimer.Stop();
        SaveNow();

        Behavior.PropertyChanged -= OnBehaviorChanged;
        foreach (var entry in Entries)
            Detach(entry);

        _iconLoader.Dispose();
    }
}
