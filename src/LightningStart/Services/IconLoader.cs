using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightningStart.Infrastructure;
using LightningStart.Interop;
using LightningStart.Models;

namespace LightningStart.Services;

internal sealed class IconLoader : IDisposable
{
    private const int RequestedPixels = 128;
    private const int EPending = unchecked((int)0x8000000A);

    private readonly BlockingCollection<IconRequest> _queue = new();
    private readonly Thread _worker;
    private bool _disposed;

    public IconLoader()
    {
        // Shell icon handlers are happiest on a single dedicated STA thread.
        // Concurrent calls from MTA thread-pool threads can return E_PENDING for
        // inbox executables (explorer.exe, notepad.exe, ...).
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "LightningStart.IconLoader"
        };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    public async Task<ImageSource?> LoadAsync(AppEntry entry)
    {
        try
        {
            var customIcon = entry.IconPath?.Trim();
            if (!string.IsNullOrWhiteSpace(customIcon))
            {
                var expanded = Environment.ExpandEnvironmentVariables(customIcon);
                if (File.Exists(expanded) && IsImageFile(expanded))
                    return LoadImageFile(expanded);

                if (File.Exists(expanded))
                    return await LoadFromShellAsync(expanded);
            }

            var target = entry.Target?.Trim();
            if (string.IsNullOrWhiteSpace(target))
                return null;

            var expandedTarget = Environment.ExpandEnvironmentVariables(target);
            var isShellPath = target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
            if (!File.Exists(expandedTarget) && !Directory.Exists(expandedTarget) && !isShellPath)
                return null;

            return await LoadFromShellAsync(isShellPath ? target : expandedTarget);
        }
        catch
        {
            return null;
        }
    }

    private Task<ImageSource?> LoadFromShellAsync(string path)
    {
        var cachePath = GetCachePath(path);
        if (File.Exists(cachePath))
        {
            var cached = TryLoadPng(cachePath);
            if (cached is not null)
                return Task.FromResult<ImageSource?>(cached);
        }

        if (_disposed)
            return Task.FromResult<ImageSource?>(null);

        var completion = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            _queue.Add(new IconRequest(path, cachePath, completion));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult<ImageSource?>(null);
        }

        return completion.Task;
    }

    private void WorkerLoop()
    {
        var comInitialized = NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.COINIT_APARTMENTTHREADED) >= 0;

        try
        {
            foreach (var request in _queue.GetConsumingEnumerable())
            {
                if (_disposed)
                {
                    request.Completion.TrySetResult(null);
                    continue;
                }

                try
                {
                    request.Completion.TrySetResult(ExtractAndCache(request.Path, request.CachePath));
                }
                catch
                {
                    request.Completion.TrySetResult(null);
                }
            }
        }
        finally
        {
            if (comInitialized)
                NativeMethods.CoUninitialize();
        }
    }

    private static ImageSource? ExtractAndCache(string path, string cachePath)
    {
        var hBitmap = ExtractHBitmap(path);
        if (hBitmap != IntPtr.Zero)
        {
            var source = BitmapFromHBitmap(hBitmap);
            if (source is not null)
            {
                TryWritePng(source, cachePath);
                return source;
            }
        }

        var fallback = ExtractViaShellInfo(path);
        if (fallback is not null)
            TryWritePng(fallback, cachePath);

        return fallback;
    }

    private static IntPtr ExtractHBitmap(string path)
    {
        try
        {
            ShellInterop.SHCreateItemFromParsingName(
                path,
                IntPtr.Zero,
                typeof(IShellItemImageFactory).GUID,
                out var factory);

            try
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var hr = factory.GetImage(
                        new SIZE(RequestedPixels, RequestedPixels),
                        SIIGBF.ICONONLY | SIIGBF.BIGGERSIZEOK,
                        out var hBitmap);

                    if (hr >= 0)
                        return hBitmap;

                    if (hBitmap != IntPtr.Zero)
                    {
                        NativeMethods.DeleteObject(hBitmap);
                        hBitmap = IntPtr.Zero;
                    }

                    // E_PENDING: the shell is still preparing the icon. A short
                    // serialized retry usually succeeds.
                    if (hr != EPending)
                        break;

                    Thread.Sleep(80 * (attempt + 1));
                }

                return IntPtr.Zero;
            }
            finally
            {
                if (Marshal.IsComObject(factory))
                    Marshal.ReleaseComObject(factory);
            }
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static BitmapSource? ExtractViaShellInfo(string path)
    {
        var info = new NativeMethods.SHFILEINFO();
        var result = NativeMethods.SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }

    private static BitmapSource? BitmapFromHBitmap(IntPtr hBitmap)
    {
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
        }
    }

    private static ImageSource? LoadImageFile(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = RequestedPixels;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? TryLoadPng(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static void TryWritePng(BitmapSource source, string cachePath)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Create(cachePath);
            encoder.Save(stream);
        }
        catch
        {
            // Icon cache is best effort only.
        }
    }

    private static string GetCachePath(string path)
    {
        string signature;
        try
        {
            var info = new FileInfo(path);
            signature = info.Exists
                ? $"{path.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}"
                : path.ToLowerInvariant();
        }
        catch
        {
            signature = path;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature))).ToLowerInvariant();
        return Path.Combine(AppPaths.IconCacheDirectory, hash[..24] + ".png");
    }

    private static bool IsImageFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _queue.CompleteAdding();

        if (_worker.IsAlive)
            _worker.Join(500);
    }

    private sealed record IconRequest(string Path, string CachePath, TaskCompletionSource<ImageSource?> Completion);
}
