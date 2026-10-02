using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EZManifest.Linux.Services;
using EZManifest.Linux.Views;
using EZManifest.Services;

namespace EZManifest.Linux.ViewModels;

public partial class BrowserDownloadItem : ObservableObject
{
    public string FileName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;

    [ObservableProperty]
    private long _receivedBytes;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private string _status = "Downloading...";

    public double Percentage => TotalBytes > 0 ? Math.Round(ReceivedBytes * 100.0 / TotalBytes, 1) : 0;
}

public partial class DepotBoxViewModel : ObservableObject
{
    private readonly DownloadsViewModel _downloadsVm;
    private readonly AppSettingsService _settingsService;

    public ObservableCollection<BrowserDownloadItem> BrowserDownloads { get; } = [];

    [ObservableProperty]
    private string _address = AppSettingsService.DefaultManifestSourceUrl;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Set by the page once the WebView control is created; the VM tells it to navigate.</summary>
    public Action<string>? NavigateRequested;

    public DepotBoxViewModel(
        DownloadsViewModel downloadsVm,
        AppSettingsService settingsService)
    {
        _downloadsVm = downloadsVm;
        _settingsService = settingsService;
        _ = LoadAddressAsync();
    }

    private async Task LoadAddressAsync()
    {
        try
        {
            var settings = await _settingsService.LoadAsync();
            string preferred = AppSettingsService.UsesPreferredManifestSource(settings)
                ? settings.PreferredManifestSourceUrl
                : AppSettingsService.DefaultManifestSourceUrl;
            if (!string.IsNullOrWhiteSpace(preferred))
                Address = preferred;
        }
        catch
        {
        }
    }

    [RelayCommand]
    private void Go()
    {
        if (!AppSettingsService.TryNormalizeHttpUrl(Address, out string url))
        {
            StatusText = "Enter a valid http(s) URL.";
            return;
        }
        Address = url;
        NavigateRequested?.Invoke(url);
    }

    [RelayCommand]
    private void GoHome()
    {
        Address = AppSettingsService.DefaultManifestSourceUrl;
        NavigateRequested?.Invoke(AppSettingsService.DefaultManifestSourceUrl);
    }

    public void OnNavigated(string url)
    {
        Address = url;
        StatusText = string.Empty;
    }

    public void OnDownloadStarted(string fullPath)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var item = new BrowserDownloadItem
            {
                FileName = Path.GetFileName(fullPath),
                FullPath = fullPath
            };
            BrowserDownloads.Insert(0, item);
            StatusText = $"Downloading {item.FileName}...";
        });
    }

    public void OnDownloadProgress(string fullPath, long received, long total)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var item = BrowserDownloads.FirstOrDefault(d => d.FullPath == fullPath);
            if (item is null)
                return;
            item.ReceivedBytes = received;
            item.TotalBytes = total;
        });
    }

    public async void OnDownloadCompleted(string fullPath)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var item = BrowserDownloads.FirstOrDefault(d => d.FullPath == fullPath);
            if (item is not null)
            {
                item.IsComplete = true;
                item.Status = "Importing...";
            }
            StatusText = $"Importing {Path.GetFileName(fullPath)}...";

            // A finished .zip is a manifest archive: feed it straight into the
            // Windows-parity import pipeline (depot selection, download, install).
            if (fullPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await _downloadsVm.ImportManifestAsync(fullPath);
                StatusText = _downloadsVm.StatusText;
            }
            else
            {
                StatusText = $"Download finished: {fullPath}";
            }

            if (item is not null)
                item.Status = "Done";
        });
    }

    public void OnDownloadCancelled(string fullPath)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var item = BrowserDownloads.FirstOrDefault(d => d.FullPath == fullPath);
            if (item is not null)
                item.Status = "Cancelled";
            StatusText = "Download cancelled";
        });
    }
}
