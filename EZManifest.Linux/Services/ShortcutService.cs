using System.Text;

namespace EZManifest.Linux.Services;

/// <summary>
/// Creates and removes .desktop shortcuts on Linux (equivalent of the Windows .lnk service).
/// </summary>
public sealed class ShortcutService
{
    public string CreateDesktopShortcut(
        string targetExePath,
        string shortcutTitle,
        string? workingDirectory = null,
        string? description = null,
        string? arguments = null)
    {
        if (string.IsNullOrWhiteSpace(targetExePath))
            throw new ArgumentException("Target executable path is required.", nameof(targetExePath));
        if (string.IsNullOrWhiteSpace(shortcutTitle))
            shortcutTitle = Path.GetFileNameWithoutExtension(targetExePath);

        string desktopPath = ResolveDesktopDirectory();
        string sanitizedTitle = SanitizeFileName(shortcutTitle);
        string shortcutPath = Path.Combine(desktopPath, $"{sanitizedTitle}.desktop");
        workingDirectory ??= Path.GetDirectoryName(targetExePath) ?? string.Empty;

        string exec = string.IsNullOrWhiteSpace(arguments)
            ? targetExePath
            : $"{targetExePath} {arguments}";

        var sb = new StringBuilder();
        sb.AppendLine("[Desktop Entry]");
        sb.AppendLine("Type=Application");
        sb.AppendLine($"Name={shortcutTitle}");
        sb.AppendLine($"Exec={exec}");
        sb.AppendLine($"Path={workingDirectory}");
        if (!string.IsNullOrWhiteSpace(description))
            sb.AppendLine($"Comment={description}");
        sb.AppendLine("Terminal=false");
        sb.AppendLine("Categories=Game;");
        File.WriteAllText(shortcutPath, sb.ToString());

        TryMarkTrusted(shortcutPath);
        return shortcutPath;
    }

    public bool RemoveDesktopShortcut(string shortcutTitle)
    {
        if (string.IsNullOrWhiteSpace(shortcutTitle))
            return false;
        try
        {
            string desktopPath = ResolveDesktopDirectory();
            string sanitizedTitle = SanitizeFileName(shortcutTitle);
            string shortcutPath = Path.Combine(desktopPath, $"{sanitizedTitle}.desktop");
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Write($"[Shortcut] Could not remove desktop shortcut: {ex.Message}");
            return false;
        }
    }

    private static void TryMarkTrusted(string path)
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "gio",
                Arguments = $"set \"{path}\" metadata::trusted true",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(2000);
        }
        catch
        {
            // gio is best-effort; the .desktop file still works on most desktops.
        }
        try
        {
            File.SetLastWriteTime(path, DateTime.Now);
        }
        catch
        {
        }
    }

    private static string ResolveDesktopDirectory()
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopPath) || !Directory.Exists(desktopPath))
            desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        return desktopPath;
    }

    private static string SanitizeFileName(string title)
    {
        char[] invalid = ['/', '\\', ':'];
        string sanitized = new(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Trim();
    }
}
