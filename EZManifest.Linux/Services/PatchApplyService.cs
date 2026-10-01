using System.Diagnostics;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace EZManifest.Linux.Services;

public readonly record struct PatchApplyProgress(string Phase, int Done, int Total, string? Current);

/// <summary>
/// Linux port of PatchApplyService: same behavior, no Windows P/Invoke;
/// 7-Zip fallback uses the system 7z binary when present.
/// </summary>
public sealed class PatchApplyService
{
    public async Task<string> ExtractArchiveAsync(
        string archivePath,
        IProgress<PatchApplyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Downloaded archive was not found.", archivePath);
        string dest = Path.Combine(
            AppPaths.DataDirectory,
            "PatchExtract",
            Path.GetFileNameWithoutExtension(archivePath) + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dest);
        try
        {
            string ext = Path.GetExtension(archivePath);
            if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(() => ExtractZip(archivePath, dest, progress, cancellationToken), cancellationToken);
            }
            else if (ext.Equals(".7z", StringComparison.OrdinalIgnoreCase) ||
                     ext.Equals(".rar", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await Task.Run(
                        () => ExtractWithSharpCompress(archivePath, dest, progress, cancellationToken),
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLog.Write(ex, "SharpCompress extract failed; trying 7-Zip");
                    progress?.Report(new PatchApplyProgress("Extracting", 0, 0, "Using 7-Zip…"));
                    await ExtractSevenZipAsync(archivePath, dest, cancellationToken);
                }
            }
            else
            {
                throw new InvalidOperationException("Game fix archives must be .zip, .7z, or .rar.");
            }
        }
        catch
        {
            TryDeleteDirectory(dest);
            throw;
        }
        return dest;
    }

    private static void ExtractZip(
        string archivePath,
        string dest,
        IProgress<PatchApplyProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        int total = archive.Entries.Count;
        int done = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = Path.GetFullPath(Path.Combine(dest, entry.FullName));
            if (!target.StartsWith(Path.GetFullPath(dest), StringComparison.Ordinal))
                continue;
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
            done++;
            if (done % 20 == 0)
                progress?.Report(new PatchApplyProgress("Extracting", done, total, entry.FullName));
        }
    }

    private static void ExtractWithSharpCompress(
        string archivePath,
        string dest,
        IProgress<PatchApplyProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var archive = ArchiveFactory.Open(archivePath);
        int total = archive.Entries.Count();
        int done = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDirectory)
                continue;
            entry.WriteToDirectory(dest, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
            done++;
            if (done % 20 == 0)
                progress?.Report(new PatchApplyProgress("Extracting", done, total, entry.Key));
        }
    }

    private static async Task ExtractSevenZipAsync(string archivePath, string dest, CancellationToken cancellationToken)
    {
        string? sevenZip = FindSevenZip();
        if (sevenZip is null)
            throw new InvalidOperationException(
                "Could not extract the archive. Install p7zip (7z) for .7z and .rar support.");
        var start = new ProcessStartInfo
        {
            FileName = sevenZip,
            Arguments = $"x -y -o\"{dest}\" -- \"{archivePath}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(start)!;
        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            AppLog.Write($"[Patch] 7-Zip exit {process.ExitCode}: {error}");
    }

    public static void UnblockFile(string? path)
    {
        // Zone.Identifier ADS is Windows-only; nothing to do on Linux.
    }

    public static string DownloadsFolder => Path.Combine(AppPaths.DataDirectory, "PatchDownloads");
    public static string ExtractFolder => Path.Combine(AppPaths.DataDirectory, "PatchExtract");

    public static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            AppLog.Write($"[Patch] Could not remove '{path}': {ex.Message}");
        }
    }

    public static async Task TryDeleteFileAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    await Task.Run(() => File.Delete(path), CancellationToken.None);
                    return;
                }
                return;
            }
            catch (Exception ex) when (attempt < 7)
            {
                AppLog.Write($"[Patch] Delete retry {attempt + 1} for '{path}': {ex.Message}");
                await Task.Delay(150 * (attempt + 1), CancellationToken.None);
            }
        }
    }

    public static async Task CleanupDownloadsAsync(string? keepPath = null)
    {
        string folder = DownloadsFolder;
        if (!Directory.Exists(folder))
            return;
        foreach (string file in Directory.EnumerateFiles(folder))
        {
            if (keepPath is not null &&
                string.Equals(file, keepPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            await TryDeleteFileAsync(file);
        }
    }

    public static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            AppLog.Write($"[Patch] Could not remove directory '{path}': {ex.Message}");
        }
    }

    private static string? FindSevenZip()
    {
        string[] candidates = ["/usr/bin/7z", "/usr/bin/7za", "/usr/bin/7zr"];
        foreach (string path in candidates)
        {
            if (File.Exists(path))
                return path;
        }
        return null;
    }
}
