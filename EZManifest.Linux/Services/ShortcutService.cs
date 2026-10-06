using System.Runtime.Versioning;
using System.Text;
using EZManifest.Services;

namespace EZManifest.Linux.Services;

/// <summary>
/// Creates and removes .desktop shortcuts on Linux (equivalent of the Windows .lnk service).
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class ShortcutService
{
    public string CreateDesktopShortcut(
        string targetExePath,
        string shortcutTitle,
        string? workingDirectory = null,
        string? description = null,
        string? arguments = null,
        string? iconPath = null)
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
            ? EscapeExecArgument(targetExePath)
            : $"{EscapeExecArgument(targetExePath)} {arguments}";

        var sb = new StringBuilder();
        sb.AppendLine("[Desktop Entry]");
        sb.AppendLine("Type=Application");
        sb.AppendLine($"Name={EscapeEntryValue(shortcutTitle)}");
        sb.AppendLine($"Exec={exec}");
        sb.AppendLine($"Path={EscapeEntryValue(workingDirectory)}");
        if (!string.IsNullOrWhiteSpace(iconPath))
            sb.AppendLine($"Icon={EscapeEntryValue(iconPath)}");
        if (!string.IsNullOrWhiteSpace(description))
            sb.AppendLine($"Comment={EscapeEntryValue(description)}");
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

    /// <summary>
    /// Quotes one Exec argument per the desktop entry spec: when the value contains
    /// a reserved character it is wrapped in double quotes, with '\', '"', '`' and
    /// '$' escaped by a backslash. Unquoted values are returned unchanged.
    /// </summary>
    private static string EscapeExecArgument(string value)
    {
        const string Reserved = " \"'\t`$<>|~&;()";
        if (value.Length != 0 && !value.Any(Reserved.Contains))
            return value;

        return "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("`", "\\`")
            .Replace("$", "\\$") + "\"";
    }

    /// <summary>Single-line string key value: line breaks would end the key.</summary>
    private static string EscapeEntryValue(string value) =>
        value.Replace("\r", " ").Replace("\n", " ");

    private static void TryMarkTrusted(string path)
    {
        try
        {
            // KDE only shows .desktop launchers that carry the execute bit;
            // GNOME tracks trust in the metadata::trusted attribute instead.
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
        }
        catch
        {
        }
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
