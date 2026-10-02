using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;

namespace EZManifest.Linux.Services;

/// <summary>
/// Replaces WebViewControl's built-in download handler, which opens a native
/// GTK file dialog that crashes inside the AppImage. Downloads start
/// automatically into the user's Downloads folder instead.
/// </summary>
/// <remarks>
/// CEF fills CefDownloadItem.FullPath only late in the download (it is empty
/// during progress updates), so items are keyed by the download id and every
/// callback receives the path chosen in OnBeforeDownload. A second click on
/// the same link while a download is running is cancelled on its first
/// progress update instead of creating a "(1)" duplicate.
/// </remarks>
public sealed class DepotBoxDownloadHandler : DownloadHandler
{
    private readonly Action<long, string> _started;
    private readonly Action<long, string, long, long> _progress;
    private readonly Action<long, string> _completed;
    private readonly Action<long, string> _cancelled;

    private readonly object _gate = new();
    private readonly Dictionary<long, string> _paths = new();
    private readonly HashSet<string> _activeNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _cancelPending = new();

    public DepotBoxDownloadHandler(
        Action<long, string> started,
        Action<long, string, long, long> progress,
        Action<long, string> completed,
        Action<long, string> cancelled)
    {
        _started = started;
        _progress = progress;
        _completed = completed;
        _cancelled = cancelled;
    }

    private static string ResolveDownloadDirectory()
    {
        try
        {
            string? home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                string downloads = Path.Combine(home, "Downloads");
                if (Directory.Exists(downloads))
                    return downloads;
                Directory.CreateDirectory(downloads);
                return downloads;
            }
        }
        catch
        {
        }
        string fallback = Path.Combine(EZManifest.Services.AppPaths.DataDirectory, "Downloads");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static string BuildUniquePath(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        int counter = 1;
        while (File.Exists(path))
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            path = Path.Combine(directory, $"{stem} ({counter}){ext}");
            counter++;
        }
        return path;
    }

    protected override void OnBeforeDownload(
        CefBrowser browser,
        CefDownloadItem downloadItem,
        string suggestedName,
        CefBeforeDownloadCallback callback)
    {
        string fileName = string.IsNullOrWhiteSpace(suggestedName) ? "download" : suggestedName;

        lock (_gate)
        {
            // Same file already downloading: continue into a temp path so CEF has
            // a target, then cancel on the first progress update and clean up.
            if (_activeNames.Contains(fileName))
            {
                long id = downloadItem.Id;
                string tempPath = Path.Combine(Path.GetTempPath(), $"ezm-dup-{id}-{fileName}");
                _paths[id] = tempPath;
                _cancelPending.Add(id);
                callback.Continue(tempPath, showDialog: false);
                return;
            }

            string path = BuildUniquePath(ResolveDownloadDirectory(), fileName);
            _paths[downloadItem.Id] = path;
            _activeNames.Add(fileName);
            _started(downloadItem.Id, path);
            callback.Continue(path, showDialog: false);
        }
    }

    protected override void OnDownloadUpdated(
        CefBrowser browser,
        CefDownloadItem downloadItem,
        CefDownloadItemCallback callback)
    {
        long id = downloadItem.Id;
        bool cancelDuplicate;
        string? path;

        lock (_gate)
        {
            cancelDuplicate = _cancelPending.Remove(id);
            _paths.TryGetValue(id, out path);
            if (cancelDuplicate || downloadItem.IsComplete || downloadItem.IsCanceled || downloadItem.IsInterrupted)
                _paths.Remove(id);
            if (downloadItem.IsComplete || downloadItem.IsCanceled || downloadItem.IsInterrupted)
                _activeNames.Remove(Path.GetFileName(path ?? downloadItem.SuggestedFileName));
        }

        if (cancelDuplicate)
        {
            callback.Cancel();
            try
            {
                if (path is not null)
                    File.Delete(path);
            }
            catch
            {
            }
            return;
        }

        if (downloadItem.IsComplete)
        {
            _completed(id, path ?? downloadItem.FullPath);
        }
        else if (downloadItem.IsCanceled || downloadItem.IsInterrupted)
        {
            _cancelled(id, path ?? downloadItem.FullPath);
        }
        else if (path is not null)
        {
            _progress(id, path, downloadItem.ReceivedBytes, downloadItem.TotalBytes);
        }
    }
}
