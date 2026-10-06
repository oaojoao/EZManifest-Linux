using System.Diagnostics;
using System.Runtime.Versioning;
using EZManifest.Models;
using EZManifest.Services;

namespace EZManifest.Linux.Services;

/// <summary>
/// Launches downloaded games on Linux: Windows executables run through Proton,
/// native Linux binaries launch directly.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class GameLauncher
{
    private readonly AppSettingsService _settingsService;
    private readonly ProtonService _protonService;

    public GameLauncher(AppSettingsService settingsService, ProtonService protonService)
    {
        _settingsService = settingsService;
        _protonService = protonService;
    }

    /// <summary>File names used by .NET games that ship a Linux entry point.</summary>
    private static readonly string[] NativeLinuxLaunchers = ["run.sh", "start.sh", "run"];

    public async Task LaunchAsync(GameEntry game, string exePath, string workingDirectory)
    {
        var settings = await _settingsService.LoadAsync();
        bool isWindowsExe = exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                            exePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                            exePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
        string? native = TryFindNativeLauncher(game.InstallPath);

        // Optional KEY=VALUE overrides (e.g. PROTON_LOG=1, DXVK_HUD=1, MANGOHUD=1):
        // the global setting first, then the per-game override so it wins on the
        // same key. Applies to both Proton and native Linux launches.
        var envOverrides = new Dictionary<string, string?>();
        foreach (var (key, value) in ParseEnvironmentVariables(settings.ProtonEnvironmentVariables))
            envOverrides[key] = value;
        foreach (var (key, value) in ParseEnvironmentVariables(game.ProtonEnvironmentVariables))
            envOverrides[key] = value;

        if (!isWindowsExe && native is not null)
        {
            LaunchDirect(native, Path.GetDirectoryName(native) ?? workingDirectory, game.LaunchOptions, envOverrides);
            return;
        }

        if (!isWindowsExe)
        {
            LaunchDirect(exePath, workingDirectory, game.LaunchOptions, envOverrides);
            return;
        }

        if (!settings.UseProton)
        {
            throw new InvalidOperationException(
                "This game is a Windows executable. Enable 'Play with Proton' in Settings, then try again.");
        }

        ProtonInstall? proton = _protonService.ResolveVersion(settings.ProtonVersion);
        if (proton is null)
        {
            throw new InvalidOperationException(
                "No Proton installation was found. Install Proton (or GE-Proton) via Steam, then try again.");
        }

        string compatDataPath = ResolvePrefixPath(game, workingDirectory);

        var psi = _protonService.BuildLaunchCommand(
            proton,
            exePath,
            workingDirectory,
            game.LaunchOptions ?? string.Empty,
            compatDataPath);

        // Merge the KEY=VALUE overrides resolved above into the Proton environment.
        foreach (var pair in envOverrides)
            psi.Environment[pair.Key] = pair.Value;

        string logPath = Path.Combine(AppPaths.DataDirectory, "game-launch.log");
        // psi.Arguments ("run <exe> <launch options>") targets a direct Process.Start;
        // for the detached shell line each token is rebuilt with POSIX-safe quoting.
        string command = $"setsid {Quote(psi.FileName)} run {Quote(exePath)}" +
            (string.IsNullOrWhiteSpace(game.LaunchOptions) ? string.Empty : $" {game.LaunchOptions}") +
            $" >> {Quote(logPath)} 2>&1";

        TraceLaunch(logPath, command, psi.Environment);
        StartDetached(command, psi.WorkingDirectory, psi.Environment);
        AppLog.Write($"[Play] Launched '{game.Name}' via {proton.Name} (detached session)");
    }

    /// <summary>
    /// Appends the exact shell command and the launch-relevant environment to
    /// game-launch.log so every Proton/native launch can be verified after the fact.
    /// </summary>
    private static void TraceLaunch(string logPath, string command, IDictionary<string, string?>? environment)
    {
        try
        {
            string env = environment is null
                ? string.Empty
                : " # " + string.Join(" ", environment
                    .Where(kv =>
                        kv.Key.StartsWith("STEAM_", StringComparison.Ordinal) ||
                        kv.Key.StartsWith("PROTON_", StringComparison.Ordinal) ||
                        kv.Key.StartsWith("WINE", StringComparison.Ordinal) ||
                        kv.Key.StartsWith("DXVK", StringComparison.Ordinal) ||
                        kv.Key.StartsWith("VKD", StringComparison.Ordinal) ||
                        kv.Key.StartsWith("MANGO", StringComparison.Ordinal) ||
                        kv.Key.StartsWith("GAMESCOPE", StringComparison.Ordinal))
                    .Select(kv => $"{kv.Key}={kv.Value}"));
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {command}{env}\n");
        }
        catch (Exception ex)
        {
            AppLog.Write($"[Play] Could not write the launch trace: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a whitespace-separated "KEY=VALUE" list. Tokens without '=' are
    /// skipped; later tokens override earlier ones.
    /// </summary>
    private static IEnumerable<(string Key, string Value)> ParseEnvironmentVariables(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        foreach (string token in text.Split(
                     (char[]?)null,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = token.IndexOf('=');
            if (eq <= 0)
                continue;
            yield return (token[..eq], token[(eq + 1)..]);
        }
    }

    /// <summary>
    /// Starts the command through <c>/bin/sh</c> in the background: the shell forks,
    /// exits immediately, and the game (in its own session via <c>setsid</c>) is
    /// reparented to init. The game is never a child of this app: closing the app
    /// or a signal sent to the app's process group cannot touch it.
    /// </summary>
    private static void StartDetached(
        string command,
        string workingDirectory,
        IDictionary<string, string?>? environment = null)
    {
        var detached = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // ArgumentList passes each argument verbatim; Arguments would be
        // re-tokenized by .NET on spaces, splitting the shell command apart.
        detached.ArgumentList.Add("-c");
        detached.ArgumentList.Add(command + " &");
        if (environment is not null)
        {
            foreach (var pair in environment)
                detached.Environment[pair.Key] = pair.Value;
        }
        // Host wrappers (VSCode terminals, AppImages, flatpak) point LD_LIBRARY_PATH
        // and LD_PRELOAD at their own bundled runtimes. A game must resolve libraries
        // against the system instead: inherited values break the Vulkan driver inside
        // wine, DXVK cannot create an instance, and the game crashes at launch.
        detached.Environment.Remove("LD_LIBRARY_PATH");
        detached.Environment.Remove("LD_PRELOAD");
        Process.Start(detached);
    }

    /// <summary>
    /// Prefix resolution order: per-game override, then the app-wide prefix from
    /// Settings, then the default per-game compatdata folder next to the install.
    /// </summary>
    private string ResolvePrefixPath(GameEntry game, string workingDirectory)
    {
        string? perGame = game.ProtonPrefixPath?.Trim();
        if (!string.IsNullOrWhiteSpace(perGame))
        {
            Directory.CreateDirectory(perGame);
            return perGame;
        }

        string? settings = _settingsService.LoadAsync().GetAwaiter().GetResult().ProtonGlobalPrefixPath?.Trim();
        if (!string.IsNullOrWhiteSpace(settings))
        {
            Directory.CreateDirectory(settings);
            return settings;
        }

        return ProtonService.GetCompatDataPath(
            !string.IsNullOrWhiteSpace(game.InstallPath) ? game.InstallPath : workingDirectory,
            game.AppId);
    }

    private static string? TryFindNativeLauncher(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            return null;
        foreach (string name in NativeLinuxLaunchers)
        {
            string candidate = Path.Combine(installPath, name);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";

    private static void LaunchDirect(
        string fileName,
        string workingDirectory,
        string? arguments,
        Dictionary<string, string?>? environment = null)
    {
        string logPath = Path.Combine(AppPaths.DataDirectory, "game-launch.log");
        string command = $"setsid {BuildDirectInvocation(fileName)}" +
            (string.IsNullOrWhiteSpace(arguments) ? string.Empty : $" {arguments}") +
            $" >> {Quote(logPath)} 2>&1";

        TraceLaunch(logPath, command, environment);
        StartDetached(command, workingDirectory, environment);
        AppLog.Write($"[Play] Launched directly (detached session): {fileName}");
    }

    /// <summary>
    /// Runs the file directly when it carries the execute bit; otherwise falls
    /// back to <c>sh</c>, since downloaded launch scripts (run.sh, start.sh...)
    /// are not always executable.
    /// </summary>
    private static string BuildDirectInvocation(string fileName)
    {
        try
        {
            UnixFileMode mode = File.GetUnixFileMode(fileName);
            bool executable = (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
            if (executable)
                return Quote(fileName);
            return $"sh {Quote(fileName)}";
        }
        catch (Exception)
        {
            return Quote(fileName);
        }
    }
}
