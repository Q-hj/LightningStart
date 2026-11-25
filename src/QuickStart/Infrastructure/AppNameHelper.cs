using System.Diagnostics;
using System.IO;

namespace QuickStart.Infrastructure;

internal static class AppNameHelper
{
    public static string Suggest(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "未命名程序";

        try
        {
            if (Directory.Exists(path))
            {
                var dirName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                return string.IsNullOrWhiteSpace(dirName) ? path : dirName;
            }

            if (File.Exists(path))
            {
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFileNameWithoutExtension(path);
                }

                var info = FileVersionInfo.GetVersionInfo(path);
                var description = info.FileDescription?.Trim();
                if (!string.IsNullOrWhiteSpace(description))
                    return Truncate(description);

                var product = info.ProductName?.Trim();
                if (!string.IsNullOrWhiteSpace(product))
                    return Truncate(product);

                return Path.GetFileNameWithoutExtension(path);
            }
        }
        catch
        {
            // Fall through to the file name based name.
        }

        var name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? path : Truncate(name);
    }

    private static string Truncate(string value)
        => value.Length <= 60 ? value : value[..60].TrimEnd() + "…";
}
