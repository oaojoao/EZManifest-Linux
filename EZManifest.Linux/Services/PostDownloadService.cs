using System.Diagnostics;
using EZManifest.Services;

namespace EZManifest.Linux.Services;

/// <summary>
/// Linux port of PostDownloadService: runs SteamAutoCrack.CLI.exe through Proton/Wine when
/// bundled next to the app, skipping gracefully when unavailable.
/// </summary>
public sealed class PostDownloadService
{
    private readonly ProtonService _protonService;

    public PostDownloadService(ProtonService protonService) =>
        _protonService = protonService;

    public string? GetCliExecutablePath()
    {
        string candidate1 = Path.Combine(AppPaths.ExeDirectory, "SteamAutoCrack.CLI", "SteamAutoCrack.CLI.exe");
        if (File.Exists(candidate1))
            return candidate1;
        string candidate2 = Path.Combine(AppContext.BaseDirectory, "SteamAutoCrack.CLI", "SteamAutoCrack.CLI.exe");
        if (File.Exists(candidate2))
            return candidate2;
        return null;
    }

    public async Task<int> RunPostDownloadCommandAsync(
        string gameName,
        string appId,
        string installPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installPath) || string.IsNullOrWhiteSpace(appId))
        {
            AppLog.Write(
                $"[PostDownload] Missing required parameters for '{gameName}' (appId='{appId}', installPath='{installPath}') — skipping.");
            throw new ArgumentException("AppID and game install path are required.");
        }
        string? cliPath = GetCliExecutablePath();
        if (string.IsNullOrWhiteSpace(cliPath))
        {
            AppLog.Write("[PostDownload] SteamAutoCrack.CLI.exe not found — skipping.");
            return 0;
        }

        string arguments = $"crack \"{installPath}\" --appid {appId}";
        AppLog.Write($"[PostDownload] Starting for '{gameName}' appId={appId}");

        ProtonInstall? proton = _protonService.ResolveVersion(null);
        ProcessStartInfo psi;
        if (proton is not null)
        {
            // SteamAutoCrack.CLI is a Windows executable: run it through Proton.
            string compatData = EZManifest.Services.ProtonService.GetCompatDataPath(installPath, appId);
            psi = _protonService.BuildLaunchCommand(proton, cliPath, installPath, arguments, compatData);
        }
        else
        {
            AppLog.Write("[PostDownload] No Proton available to run SteamAutoCrack.CLI — skipping.");
            return 0;
        }
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var exitTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                AppLog.Write($"[PostDownload] {e.Data}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                AppLog.Write($"[PostDownload:err] {e.Data}");
        };
        process.Exited += (_, _) => exitTcs.TrySetResult();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await using (cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            exitTcs.TrySetCanceled(cancellationToken);
        }))
        {
            await exitTcs.Task;
        }
        AppLog.Write($"[PostDownload] Exit code {process.ExitCode} for '{gameName}'");
        return process.ExitCode;
    }
}
