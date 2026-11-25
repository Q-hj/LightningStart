using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using QuickStart.Interop;
using QuickStart.Models;
using QuickStart.Services;
using QuickStart.ViewModels;

namespace QuickStart.Views;

public partial class FlyoutWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _watchTimer;
    private readonly DispatcherTimer _errorTimer;
    private NativeMethods.POINT _hotspot;
    private DateTime? _outsideSince;
    private bool _autoHide = true;

    public event EventHandler? MainViewRequested;

    public FlyoutWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;

        InitializeComponent();

        DataContext = viewModel;
        Width = CalculateWidth();

        _watchTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _watchTimer.Tick += (_, _) => WatchCursor();

        _errorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _errorTimer.Tick += (_, _) =>
        {
            _errorTimer.Stop();
            HideError();
        };

        viewModel.Behavior.PropertyChanged += OnBehaviorChanged;
        Closing += FlyoutWindow_Closing;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.AddExtendedStyles(
            handle,
            NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
    }

    public void ShowPanel(bool autoHide = true)
    {
        RefreshMissingStates();
        _autoHide = autoHide;

        if (!IsVisible)
        {
            NativeMethods.GetCursorPos(out _hotspot);
            _outsideSince = null;

            UpdateMetrics();
            Opacity = 0;
            Show();
            UpdateLayout();
            PositionPanel(_hotspot);
            Opacity = 1;
        }

        if (_autoHide)
            _watchTimer.Start();
        else
            _watchTimer.Stop();
    }

    public void HidePanel()
    {
        _watchTimer.Stop();
        _errorTimer.Stop();
        _outsideSince = null;
        HideError();

        if (IsVisible)
            Hide();
    }

    private void RefreshMissingStates()
    {
        foreach (var entry in _viewModel.Entries)
            entry.IsTargetMissing = !entry.TargetExists();
    }

    private void OnBehaviorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(BehaviorConfig.Columns) or nameof(BehaviorConfig.IconSize)))
            return;

        UpdateMetrics();

        if (!IsVisible)
            return;

        UpdateLayout();
        PositionPanel(_hotspot);
    }

    private double CalculateWidth()
    {
        var cell = Math.Max(72, _viewModel.Behavior.IconSize + 36);
        // Two 12px shadow margins plus two 12px card paddings.
        return _viewModel.Behavior.Columns * cell + 48;
    }

    private void UpdateMetrics()
    {
        Width = CalculateWidth();
    }

    private void WatchCursor()
    {
        if (!IsVisible || !_autoHide)
            return;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        NativeMethods.GetCursorPos(out var point);
        if (!NativeMethods.GetWindowRect(handle, out var rect))
            return;

        var insidePanel = point.X >= rect.Left && point.X <= rect.Right &&
                          point.Y >= rect.Top && point.Y <= rect.Bottom;

        var nearTrayIcon = Math.Abs(point.X - _hotspot.X) <= 48 &&
                           Math.Abs(point.Y - _hotspot.Y) <= 48;

        if (insidePanel || nearTrayIcon)
        {
            _outsideSince = null;
            return;
        }

        _outsideSince ??= DateTime.UtcNow;
        if ((DateTime.UtcNow - _outsideSince.Value).TotalMilliseconds > 350)
            HidePanel();
    }

    private void PositionPanel(NativeMethods.POINT anchor)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        var monitor = NativeMethods.MonitorFromPoint(anchor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
            return;

        // Use the target monitor's effective DPI so a mixed-DPI setup positions the
        // panel correctly even before the window has been moved there.
        var dpi = 96u;
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) != 0 || dpiX == 0)
        {
            var windowDpi = NativeMethods.GetDpiForWindow(handle);
            if (windowDpi != 0)
                dpi = windowDpi;
        }
        else
        {
            dpi = dpiX;
        }

        var scale = dpi / 96.0;
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        var workArea = monitorInfo.rcWork;
        var monitorArea = monitorInfo.rcMonitor;

        var x = anchor.X - width / 2;
        x = Math.Clamp(x, workArea.Left + 8, Math.Max(workArea.Left + 8, workArea.Right - width - 8));

        // Detect a vertical taskbar (taskbar docked left/right). In that case the
        // tray icon lives on the side edge, so a flyout above the pointer looks
        // too high. Place it beside the taskbar and center it on the pointer.
        var onLeftTaskbar = workArea.Left > monitorArea.Left && anchor.X <= workArea.Left;
        var onRightTaskbar = workArea.Right < monitorArea.Right && anchor.X >= workArea.Right;

        int y;
        if (onLeftTaskbar || onRightTaskbar)
        {
            y = anchor.Y - height / 2;
            y = Math.Clamp(
                y,
                workArea.Top + 8,
                Math.Max(workArea.Top + 8, workArea.Bottom - height - 8));
        }
        else
        {
            // Keep the window fully above/below the pointer so its transparent
            // shadow margin never covers the tray icon's click area.
            const int gap = 12;
            var above = anchor.Y - height - gap;
            var below = anchor.Y + gap;

            if (above >= workArea.Top + 8)
                y = above;
            else if (below + height <= workArea.Bottom - 8)
                y = below;
            else
                y = Math.Clamp(
                    above,
                    workArea.Top + 8,
                    Math.Max(workArea.Top + 8, workArea.Bottom - height - 8));
        }

        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;

        _errorTimer.Stop();
        _errorTimer.Start();

        Dispatcher.InvokeAsync(() =>
        {
            if (!IsVisible)
                return;

            UpdateLayout();
            PositionPanel(_hotspot);
        }, DispatcherPriority.Loaded);
    }

    private void HideError()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;

        if (!IsVisible)
            return;

        Dispatcher.InvokeAsync(() =>
        {
            if (!IsVisible)
                return;

            UpdateLayout();
            PositionPanel(_hotspot);
        }, DispatcherPriority.Loaded);
    }

    private void Entry_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AppEntry entry)
            return;

        if (AppLauncher.TryLaunch(entry, out var error))
        {
            HidePanel();
        }
        else if (!string.IsNullOrEmpty(error))
        {
            ShowError("启动失败：" + error);
        }
    }

    private void OpenMainView_Click(object sender, RoutedEventArgs e)
    {
        HidePanel();
        MainViewRequested?.Invoke(this, EventArgs.Empty);
    }

    private void FlyoutWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (App.IsShuttingDown)
            return;

        e.Cancel = true;
        HidePanel();
    }
}
