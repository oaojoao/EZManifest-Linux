using System.Diagnostics;
using EZManifest.Models;
using EZManifest.Services;

namespace EZManifest.Linux.Services;

/// <summary>
/// Launches downloaded games on Linux: Windows executables run through Proton,
/// native Linux binaries launch directly.
/// </summary>
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

        if (!isWindowsExe && native is not null)
        {
            LaunchDirect(native, Path.GetDirectoryName(native) ?? workingDirectory, game.LaunchOptions);
            return;
        }

        if (!isWindowsExe)
        {
            LaunchDirect(exePath, workingDirectory, game.LaunchOptions);
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

        string compatDataPath = ProtonService.GetCompatDataPath(
            !string.IsNullOrWhiteSpace(game.InstallPath) ? game.InstallPath : workingDirectory,
            game.AppId);

        ProcessStartInfo psi = _protonService.BuildLaunchCommand(
            proton,
            exePath,
            workingDirectory,
            game.LaunchOptions ?? string.Empty,
            compatDataPath);
        Process.Start(psi);
        AppLog.Write($"[Play] Launched '{game.Name}' via {proton.Name}");
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

    private static void LaunchDirect(string fileName, string workingDirectory, string? arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true
        };
        Process.Start(psi);
        AppLog.Write($"[Play] Launched directly: {fileName}");
    }
}
