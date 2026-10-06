using System.Diagnostics;

namespace EZManifest.Services;

/// <summary>
/// A Proton build discovered on this machine (official Steam Proton, GE-Proton, or any
/// other fork that ships a "proton" launcher script).
/// </summary>
public sealed record ProtonInstall(string Name, string ScriptPath);

/// <summary>
/// Discovers Proton versions installed on the OS and builds launch commands for
/// Windows game executables. On Windows the app keeps launching executables directly,
/// so every member is a no-op there.
/// </summary>
public sealed class ProtonService
{
    public const string AutoVersion = "Auto";

    private static readonly Lazy<IReadOnlyList<ProtonInstall>> InstalledVersions = new(DiscoverInstalledVersions);

    /// <summary>Proton launching is only relevant on Linux; Windows keeps direct launches.</summary>
    public static bool IsSupported => !OperatingSystem.IsWindows();

    public IReadOnlyList<ProtonInstall> GetInstalledVersions() => InstalledVersions.Value;

    public static string? SteamHome
    {
        get
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                return null;
            string[] candidates =
            [
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".steam", "root"),
            ];
            return candidates.FirstOrDefault(Directory.Exists);
        }
    }

    private static IEnumerable<string> SearchRoots
    {
        get
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                yield break;

            // ~/.steam/steam, ~/.steam/root and ~/.local/share/Steam are usually
            // symlinks to the same Steam library: resolve them and skip duplicates
            // so each Proton build is discovered exactly once.
            var seenRoots = new HashSet<string>(StringComparer.Ordinal);
            foreach (string libraryRoot in new[]
            {
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".steam", "root"),
            })
            {
                string resolved;
                try
                {
                    if (!Directory.Exists(libraryRoot))
                        continue;
                    // Path.GetFullPath only normalizes the string and keeps symlinks:
                    // the final target must be resolved or the same Proton build is
                    // discovered once per alias.
                    FileSystemInfo? target = Directory.ResolveLinkTarget(libraryRoot, returnFinalTarget: true);
                    resolved = target?.FullName ?? Path.GetFullPath(libraryRoot);
                }
                catch
                {
                    continue;
                }

                if (!seenRoots.Add(resolved))
                    continue;
                yield return Path.Combine(resolved, "steamapps", "common");
                yield return Path.Combine(resolved, "compatibilitytools.d");
            }
        }
    }

    private static IReadOnlyList<ProtonInstall> DiscoverInstalledVersions()
    {
        var found = new List<ProtonInstall>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in SearchRoots)
        {
            string[] entries;
            try
            {
                if (!Directory.Exists(root))
                    continue;
                entries = Directory.GetDirectories(root);
            }
            catch (Exception ex)
            {
                AppLog.Write($"[Proton] Could not read '{root}': {ex.Message}");
                continue;
            }

            foreach (string dir in entries)
            {
                string name = Path.GetFileName(dir);
                if (!name.Contains("Proton", StringComparison.OrdinalIgnoreCase))
                    continue;

                string script = Path.Combine(dir, "proton");
                if (!File.Exists(script))
                    continue;
                if (!seen.Add(Path.GetFullPath(dir)))
                    continue;

                found.Add(new ProtonInstall(name, script));
            }
        }

        found.Sort((a, b) => string.CompareOrdinal(b.Name, a.Name));
        AppLog.Write($"[Proton] Discovered {found.Count} Proton version(s): {string.Join(", ", found.Select(v => v.Name))}");
        return found;
    }

    /// <summary>
    /// Picks the Proton build to use: the preferred version when it is still installed,
    /// otherwise the newest discovered build. Returns null when nothing is installed.
    /// </summary>
    public ProtonInstall? ResolveVersion(string? preferredVersion)
    {
        IReadOnlyList<ProtonInstall> versions = InstalledVersions.Value;
        if (versions.Count == 0)
            return null;

        string? preferred = preferredVersion?.Trim();
        if (!string.IsNullOrWhiteSpace(preferred) && !string.Equals(preferred, AutoVersion, StringComparison.OrdinalIgnoreCase))
        {
            ProtonInstall? match = versions.FirstOrDefault(v =>
                string.Equals(v.Name, preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;

            AppLog.Write($"[Proton] Preferred version '{preferred}' is not installed; falling back to {versions[0].Name}");
        }

        return versions[0];
    }

    /// <summary>
    /// Builds the process that launches a Windows executable through Proton.
    /// compatDataPath is the WINE prefix Proton uses for the game.
    /// </summary>
    public ProcessStartInfo BuildLaunchCommand(
        ProtonInstall proton,
        string exePath,
        string workingDirectory,
        string arguments,
        string compatDataPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = proton.ScriptPath,
            Arguments = $"run {Quote(exePath)}{(string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments)}",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };

        psi.Environment["STEAM_COMPAT_DATA_PATH"] = compatDataPath;

        string? steamHome = SteamHome;
        if (!string.IsNullOrEmpty(steamHome))
            psi.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = steamHome;

        AppLog.Write($"[Proton] Launching '{exePath}' with {proton.Name} (prefix: {compatDataPath})");
        return psi;
    }

    /// <summary>
    /// Default WINE prefix location for a game: a compatdata folder next to the game folder.
    /// </summary>
    public static string GetCompatDataPath(string gameFolder, string appId)
    {
        string compatDataPath;
        try
        {
            string? parent = Path.GetDirectoryName(Path.GetFullPath(gameFolder));
            compatDataPath = string.IsNullOrWhiteSpace(parent)
                ? Path.Combine(gameFolder, "compatdata")
                : Path.Combine(parent, "compatdata", appId);
        }
        catch (Exception ex)
        {
            AppLog.Write($"[Proton] Could not derive compatdata parent from '{gameFolder}': {ex.Message}");
            compatDataPath = Path.Combine(gameFolder, "compatdata");
        }

        // Proton opens pfx.lock inside the prefix without creating parents,
        // so a missing compatdata directory crashes the launch.
        try
        {
            Directory.CreateDirectory(compatDataPath);
            string pfxDir = Path.Combine(compatDataPath, "pfx");
            if (!Directory.Exists(pfxDir))
                Directory.CreateDirectory(pfxDir);
        }
        catch (Exception ex)
        {
            AppLog.Write($"[Proton] Could not create compatdata directory '{compatDataPath}': {ex.Message}");
        }

        return compatDataPath;
    }

    private static string Quote(string value) => $"\"{value.TrimEnd(Path.DirectorySeparatorChar).Replace("\"", "\\\"")}\"";
}
