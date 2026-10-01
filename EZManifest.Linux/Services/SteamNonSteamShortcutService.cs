using System.Diagnostics;
using System.Text;
using EZManifest.Models;
using EZManifest.Services;

namespace EZManifest.Linux.Services;

public sealed record SteamShortcutAddResult(
    int AccountsUpdated,
    int AlreadyPresent,
    int AccountCount,
    bool SteamWasRunning);

/// <summary>
/// Adds games to Steam's non-Steam shortcuts on Linux. The shortcuts.vdf format is identical
/// to Windows; only Steam discovery and process-tree logic are Linux-specific.
/// </summary>
public sealed class SteamNonSteamShortcutService
{
    private readonly SteamMetadataService _steamMetadata;

    public SteamNonSteamShortcutService(SteamMetadataService steamMetadata) =>
        _steamMetadata = steamMetadata;

    public static bool IsSteamRunning() =>
        Process.GetProcessesByName("steam").Length > 0;

    public static bool IsSteamUiRunning() => IsSteamRunning();

    public async Task CloseSteamAsync(CancellationToken cancellationToken = default)
    {
        string? steamExe = FindSteamBinary();
        if (!string.IsNullOrWhiteSpace(steamExe) && File.Exists(steamExe))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = steamExe,
                    Arguments = "-shutdown",
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, "[SteamShortcut] steam -shutdown failed");
            }
        }

        var started = Stopwatch.StartNew();
        while (IsSteamRunning() && started.Elapsed < TimeSpan.FromSeconds(20))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(400, cancellationToken);
        }
        if (IsSteamRunning())
            throw new InvalidOperationException("Steam is still running. Close it, then try again.");
    }

    public Task RestartSteamAsync(CancellationToken cancellationToken = default)
    {
        // steam:// URLs are handled by xdg-open on Linux desktops.
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = "steam://open/library",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "[SteamShortcut] Could not reopen Steam via steam:// URL");
        }
        return Task.CompletedTask;
    }

    public async Task<SteamShortcutAddResult> AddToAllAccountsAsync(
        GameEntry game,
        string exePath,
        CancellationToken cancellationToken = default)
    {
        string steamRoot = FindSteamRoot()
            ?? throw new DirectoryNotFoundException("Steam was not found. Install Steam or start it once, then try again.");
        string userdata = Path.Combine(steamRoot, "userdata");
        if (!Directory.Exists(userdata))
            throw new DirectoryNotFoundException($"Steam userdata was not found:\n{userdata}");
        string[] accounts = Directory.GetDirectories(userdata)
            .Where(path => Path.GetFileName(path).All(char.IsDigit))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (accounts.Length == 0)
            throw new DirectoryNotFoundException("No Steam accounts were found in userdata.");

        exePath = Path.GetFullPath(exePath);
        if (!File.Exists(exePath))
            throw new FileNotFoundException("Game executable was not found.", exePath);

        string startDir = Path.GetDirectoryName(exePath) ?? string.Empty;
        if (!startDir.EndsWith(Path.DirectorySeparatorChar))
            startDir += Path.DirectorySeparatorChar;
        string quotedExe = QuotePath(exePath);
        string quotedStart = QuotePath(startDir);
        string appName = string.IsNullOrWhiteSpace(game.Name)
            ? Path.GetFileNameWithoutExtension(exePath)
            : game.Name;
        uint appId = ShortcutAppId(quotedExe, appName);
        int appIdSigned = unchecked((int)appId);
        string launchOptions = game.LaunchOptions ?? string.Empty;
        bool steamWasRunning = IsSteamUiRunning();

        int updated = 0;
        int already = 0;
        foreach (string account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string configDir = Path.Combine(account, "config");
            string shortcutsPath = Path.Combine(configDir, "shortcuts.vdf");
            SteamShortcutsVdf.Node root = SteamShortcutsVdf.LoadOrCreate(shortcutsPath);
            SteamShortcutsVdf.Node? existing = FindExisting(root, quotedExe, appName);
            if (existing is not null)
            {
                existing.SetInt("appid", appIdSigned);
                existing.SetString("StartDir", quotedStart);
                existing.SetString("LaunchOptions", launchOptions);
                already++;
            }
            else
            {
                int index = NextIndex(root);
                root.Children.Add(SteamShortcutsVdf.NewShortcut(
                    index,
                    appIdSigned,
                    appName,
                    quotedExe,
                    quotedStart,
                    icon: string.Empty,
                    launchOptions));
            }
            SteamShortcutsVdf.Save(shortcutsPath, root);
            updated++;
            AppLog.Write($"[SteamShortcut] '{appName}' → account {Path.GetFileName(account)} appId={appId}");
        }

        return new SteamShortcutAddResult(updated, already, accounts.Length, steamWasRunning);
    }

    public Task RemoveFromAllAccountsAsync(GameEntry game, CancellationToken cancellationToken = default)
    {
        try
        {
            RemoveFromAllAccounts(game, cancellationToken);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"[SteamShortcut] Remove skipped for '{game.Name}'");
        }
        return Task.CompletedTask;
    }

    private void RemoveFromAllAccounts(GameEntry game, CancellationToken cancellationToken)
    {
        string? steamRoot = FindSteamRoot();
        if (string.IsNullOrWhiteSpace(steamRoot))
            return;
        string userdata = Path.Combine(steamRoot, "userdata");
        if (!Directory.Exists(userdata))
            return;
        string appName = game.Name ?? string.Empty;
        string exePath = string.IsNullOrWhiteSpace(game.StartLocation)
            ? string.Empty
            : Path.GetFullPath(game.StartLocation);

        foreach (string account in Directory.GetDirectories(userdata).Where(path => Path.GetFileName(path).All(char.IsDigit)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string shortcutsPath = Path.Combine(account, "config", "shortcuts.vdf");
            if (!File.Exists(shortcutsPath))
                continue;
            SteamShortcutsVdf.Node root = SteamShortcutsVdf.LoadOrCreate(shortcutsPath);
            var removed = root.Children
                .Where(child => child.Type == 0x00 && MatchesShortcut(child, appName, exePath))
                .ToList();
            if (removed.Count == 0)
                continue;
            foreach (SteamShortcutsVdf.Node node in removed)
            {
                root.Children.Remove(node);
            }
            SteamShortcutsVdf.Save(shortcutsPath, root);
            AppLog.Write(
                $"[SteamShortcut] Removed {removed.Count} shortcut(s) for '{appName}' " +
                $"from account {Path.GetFileName(account)}");
        }
    }

    private static bool MatchesShortcut(SteamShortcutsVdf.Node node, string appName, string exePath)
    {
        string name = node.GetString("AppName");
        if (!string.IsNullOrWhiteSpace(appName) &&
            name.Equals(appName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            string exe = NormalizeExe(node.GetString("Exe"));
            if (exe.Equals(NormalizeExe(QuotePath(exePath)), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string NormalizeExe(string value) =>
        value.Trim().Trim('"').Replace("\\", "/").TrimEnd('/');

    private static SteamShortcutsVdf.Node? FindExisting(
        SteamShortcutsVdf.Node root,
        string quotedExe,
        string appName)
    {
        foreach (SteamShortcutsVdf.Node child in root.Children)
        {
            if (child.Type != 0x00)
                continue;
            string exe = NormalizeExe(child.GetString("Exe"));
            if (exe.Equals(NormalizeExe(quotedExe), StringComparison.OrdinalIgnoreCase) ||
                child.GetString("AppName").Equals(appName, StringComparison.OrdinalIgnoreCase))
                return child;
        }
        return null;
    }

    private static int NextIndex(SteamShortcutsVdf.Node root)
    {
        int max = -1;
        foreach (SteamShortcutsVdf.Node child in root.Children)
        {
            if (int.TryParse(child.Name.AsSpan(), out int index) && index > max)
                max = index;
        }
        return max + 1;
    }

    public static uint ShortcutAppId(string quotedExe, string appName)
    {
        uint crc = Crc32(Encoding.UTF8.GetBytes(quotedExe + appName));
        return crc | 0x80000000u;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    private static string? FindSteamRoot()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        [
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".steam", "root"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam")
        ];
        foreach (string candidate in candidates)
        {
            if (Directory.Exists(Path.Combine(candidate, "userdata")))
                return candidate;
        }
        return null;
    }

    private static string? FindSteamBinary()
    {
        string? root = FindSteamRoot();
        if (root is null)
            return null;
        string candidate = Path.Combine(root, "steam.sh");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string QuotePath(string value) => $"\"{value}\"";
}
