using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;

namespace EZManifest.Linux.Services;

/// <summary>
/// Replaces WebViewControl's built-in download handler, which opens a native
/// GTK file dialog (showDialog: true) that crashes inside the AppImage.
/// Downloads start automatically into the user's Downloads folder instead.
/// </summary>
public sealed class DepotBoxDownloadHandler : DownloadHandler
{
    private readonly Action<string> _started;
    private readonly Action<string, long, long> _progress;
    private readonly Action<string> _completed;
    private readonly Action<string> _cancelled;

    public DepotBoxDownloadHandler(
        Action<string> started,
        Action<string, long, long> progress,
        Action<string> completed,
        Action<string> cancelled)
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

    protected override void OnBeforeDownload(
        CefBrowser browser,
        CefDownloadItem downloadItem,
        string suggestedName,
        CefBeforeDownloadCallback callback)
    {
        string directory = ResolveDownloadDirectory();
        string fileName = string.IsNullOrWhiteSpace(suggestedName) ? "download" : suggestedName;
        string path = Path.Combine(directory, fileName);
        int counter = 1;
        while (File.Exists(path))
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            path = Path.Combine(directory, $"{stem} ({counter}){ext}");
            counter++;
        }
        _started(path);
        callback.Continue(path, showDialog: false);
    }

    protected override void OnDownloadUpdated(
        CefBrowser browser,
        CefDownloadItem downloadItem,
        CefDownloadItemCallback callback)
    {
        if (downloadItem.IsComplete)
            _completed(downloadItem.FullPath);
        else if (downloadItem.IsCanceled)
            _cancelled(downloadItem.FullPath);
        else
            _progress(downloadItem.FullPath, downloadItem.ReceivedBytes, downloadItem.TotalBytes);
    }
}
