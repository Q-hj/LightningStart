using System.Runtime.InteropServices;
using System.Windows.Interop;
using QuickStart.Interop;

namespace QuickStart.Services;

/// <summary>
/// Minimal shell notification area icon wrapper using Shell_NotifyIcon version 4.
/// Version 4 gives us NIN_POPUPOPEN / NIN_POPUPCLOSE, which makes hover handling reliable.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private const uint IconId = 1;

    private readonly HwndSource _source;
    private readonly int _callbackMessage = NativeMethods.WM_APP + 42;
    private readonly uint _taskbarCreatedMessage;
    private NativeMethods.NOTIFYICONDATA _iconData;
    private IntPtr _smallIcon;
    private IntPtr _largeIcon;
    private bool _version4;
    private bool _disposed;

    public event Action? LeftClick;
    public event Action? HoverOpen;
    public event Action? HoverClose;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        var parameters = new HwndSourceParameters("QuickStart.TrayMessageWindow")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = unchecked((int)NativeMethods.WS_EX_TOOLWINDOW),
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        LoadIcons();

        _iconData = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = IconId,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP,
            uCallbackMessage = (uint)_callbackMessage,
            hIcon = _smallIcon,
            szTip = "QuickStart 快捷启动（右键打开设置）",
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };

        if (!AddIcon())
            throw new InvalidOperationException($"Shell_NotifyIcon(NIM_ADD) 失败（错误码 {Marshal.GetLastWin32Error()}）");
    }

    private void LoadIcons()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
            {
                NativeMethods.ExtractIconEx(executable, 0, out _largeIcon, out _smallIcon, 1);
            }
        }
        catch
        {
            // Fall back to the stock application icon below.
        }

        if (_smallIcon == IntPtr.Zero)
            _smallIcon = NativeMethods.LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
    }

    private bool AddIcon()
    {
        if (_disposed)
            return false;

        var data = _iconData;
        data.hIcon = _smallIcon;
        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data))
            return false;

        data.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
        _version4 = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);

        _iconData = data;
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _callbackMessage)
        {
            var notification = (int)(lParam.ToInt64() & 0xFFFF);
            switch (notification)
            {
                case NativeMethods.NIN_SELECT:
                case NativeMethods.NIN_KEYSELECT:
                    LeftClick?.Invoke();
                    handled = true;
                    break;

                case NativeMethods.NIN_POPUPOPEN:
                case NativeMethods.WM_MOUSEMOVE:
                    HoverOpen?.Invoke();
                    handled = true;
                    break;

                case NativeMethods.NIN_POPUPCLOSE:
                case NativeMethods.WM_MOUSELEAVE:
                    HoverClose?.Invoke();
                    handled = true;
                    break;

                case NativeMethods.WM_CONTEXTMENU:
                case NativeMethods.WM_RBUTTONUP:
                    ShowContextMenu();
                    handled = true;
                    break;

                case NativeMethods.WM_LBUTTONUP when !_version4:
                    LeftClick?.Invoke();
                    handled = true;
                    break;
            }
        }
        else if (_taskbarCreatedMessage != 0 && (uint)msg == _taskbarCreatedMessage)
        {
            // Explorer restarted: the notification icon must be re-added.
            AddIcon();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 1, "打开设置");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 2, "退出 QuickStart");

            NativeMethods.GetCursorPos(out var point);
            NativeMethods.SetForegroundWindow(_source.Handle);

            var command = NativeMethods.TrackPopupMenu(
                menu,
                NativeMethods.TPM_LEFTALIGN | NativeMethods.TPM_RIGHTBUTTON |
                NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY,
                point.X,
                point.Y,
                0,
                _source.Handle,
                IntPtr.Zero);

            NativeMethods.PostMessage(_source.Handle, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (command == 1)
                SettingsRequested?.Invoke();
            else if (command == 2)
                ExitRequested?.Invoke();
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            var data = _iconData;
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
        }
        catch
        {
            // Ignore shell shutdown races.
        }

        try
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
        }
        catch
        {
            // Ignore.
        }

        if (_smallIcon != IntPtr.Zero)
            NativeMethods.DestroyIcon(_smallIcon);
        if (_largeIcon != IntPtr.Zero)
            NativeMethods.DestroyIcon(_largeIcon);

        _smallIcon = IntPtr.Zero;
        _largeIcon = IntPtr.Zero;
    }
}
