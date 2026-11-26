using System.Threading;
using System.Windows;
using System.Windows.Threading;
using LightningStart.Infrastructure;
using LightningStart.Interop;
using LightningStart.Services;
using LightningStart.ViewModels;
using LightningStart.Views;

namespace LightningStart;

public partial class App : Application
{
    private const string MutexName = @"Local\LightningStart.SingleInstance";
    private const string ShowEventName = @"Local\LightningStart.ShowSettings";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private TrayIconService? _tray;
    private FlyoutWindow? _flyout;
    private MainWindow? _mainWindow;
    private DispatcherTimer? _hoverTimer;
    private NativeMethods.POINT _hoverAnchor;
    private DateTime _lastToggleAt = DateTime.MinValue;

    public static bool IsShuttingDown { get; private set; }

    internal MainViewModel ViewModel { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterExceptionHandlers();

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var openSettings = e.Args.Any(arg => arg.Equals("--settings", StringComparison.OrdinalIgnoreCase));
        var debugFlyout = e.Args.Any(arg => arg.Equals("--flyout", StringComparison.OrdinalIgnoreCase));

        _mutex = new Mutex(initiallyOwned: true, MutexName, out var isNewInstance);
        if (!isNewInstance)
        {
            // Only an explicit --settings request wakes the running instance.
            // A plain launch must never pop up the main window.
            if (openSettings)
                SignalExistingInstance();

            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => Dispatcher.InvokeAsync(ShowMainWindow, DispatcherPriority.Normal),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        AppPaths.EnsureCreated();
        ViewModel = new MainViewModel();

        _flyout = new FlyoutWindow(ViewModel);
        _flyout.MainViewRequested += (_, _) => ShowMainWindow();
        _flyout.LaunchFailed += ShowLaunchFailure;

        try
        {
            _tray = new TrayIconService();
            _tray.LeftClick += ToggleFlyout;
            _tray.HoverOpen += OnTrayHoverOpen;
            _tray.HoverClose += OnTrayHoverClose;
            _tray.SettingsRequested += ShowMainWindow;
            _tray.ExitRequested += ExitApplication;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "无法创建托盘图标：" + ex.Message,
                "闪电启动",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // Startup is tray-only by default. The settings window is opened on demand
        // (tray menu, panel button, relaunching the exe or the --settings argument).
        if (openSettings)
            ShowMainWindow();

        if (_tray is not null && ViewModel.IsFirstRun && !openSettings)
        {
            Dispatcher.InvokeAsync(
                () => _tray?.ShowBalloon(
                    "闪电启动已在托盘运行",
                    "右键托盘图标打开主视图；如果看不到图标，请在任务栏设置中显示。"),
                DispatcherPriority.ApplicationIdle);
        }

        if (debugFlyout)
            Dispatcher.InvokeAsync(() => _flyout?.ShowPanel(autoHide: false), DispatcherPriority.ApplicationIdle);
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLogger.Write("Dispatcher", e.Exception);

        try
        {
            MessageBox.Show(
                $"程序遇到未处理的错误，已记录到：\n{CrashLogger.LogFilePath}\n\n{e.Exception.Message}",
                "闪电启动",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // The log has already been written; don't mask the original failure.
        }

        e.Handled = true;
        Shutdown(1);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            CrashLogger.Write("AppDomain", exception);
        else
            CrashLogger.Write("AppDomain", new InvalidOperationException(
                Convert.ToString(e.ExceptionObject) ?? "未知错误"));
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLogger.Write("TaskScheduler", e.Exception);
        e.SetObserved();
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
            showEvent.Set();
        }
        catch
        {
            // The other instance may be shutting down already.
        }
    }

    private void ShowLaunchFailure(string message)
    {
        if (IsShuttingDown)
            return;

        if (_tray is not null)
        {
            _tray.ShowBalloon("启动失败", message);
            return;
        }

        MessageBox.Show(message, "闪电启动", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowMainWindow()
    {
        _hoverTimer?.Stop();
        _flyout?.HidePanel();

        _mainWindow ??= new MainWindow { DataContext = ViewModel };

        if (!_mainWindow.IsVisible)
            _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;

        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();
    }

    private void ToggleFlyout()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastToggleAt).TotalMilliseconds < 300)
            return;
        _lastToggleAt = now;

        if (_flyout is null)
            return;

        if (_flyout.IsVisible)
            _flyout.HidePanel();
        else
            _flyout.ShowPanel();
    }

    private void OnTrayHoverOpen()
    {
        if (ViewModel.Behavior.Trigger != "Hover")
            return;
        if (_flyout is { IsVisible: true })
            return;
        if (_hoverTimer is { IsEnabled: true })
            return;

        NativeMethods.GetCursorPos(out _hoverAnchor);

        _hoverTimer ??= CreateHoverTimer();
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, ViewModel.Behavior.HoverDelayMs));
        _hoverTimer.Start();
    }

    private DispatcherTimer CreateHoverTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            NativeMethods.GetCursorPos(out var point);
            if (Math.Abs(point.X - _hoverAnchor.X) <= 48 && Math.Abs(point.Y - _hoverAnchor.Y) <= 48)
                _flyout?.ShowPanel();
        };
        return timer;
    }

    private void OnTrayHoverClose() => _hoverTimer?.Stop();

    public void ExitApplication()
    {
        if (IsShuttingDown)
            return;

        IsShuttingDown = true;

        try
        {
            _flyout?.Close();
        }
        catch
        {
            // Ignore shutdown races.
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hoverTimer?.Stop();

        try { _tray?.Dispose(); } catch { }
        try { ViewModel?.Dispose(); } catch { }

        _showWait?.Unregister(null);
        try { _showEvent?.Dispose(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }

        base.OnExit(e);
    }
}
