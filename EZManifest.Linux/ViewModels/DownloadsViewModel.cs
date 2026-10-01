using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EZManifest.Linux.Services;
using EZManifest.Models;
using EZManifest.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EZManifest.Linux.ViewModels;

public partial class DepotRow : ObservableObject
{
    public string DepotId { get; init; } = string.Empty;
    public string ManifestId { get; init; } = string.Empty;
    public string ManifestPath { get; init; } = string.Empty;
    public string HexKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string SizeText { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

public partial class DownloadsViewModel : ObservableObject
{
    private readonly ManifestArchiveService _archiveService;
    private readonly LuaManifestParser _luaParser;
    private readonly SteamDepotMetadataService _depotMetadata;
    private readonly AppSettingsService _settingsService;
    private readonly GameLibraryService _gameLibrary;
    private readonly GameInstallPathService _installPathService;
    private readonly PostDownloadService _postDownload;
    private readonly AppMessageBoxService _messageBoxService;
    private readonly FileExplorerPickerService _filePicker;
    private readonly AppNotificationService _notifications;
    private readonly IServiceProvider _services;

    public ObservableCollection<DepotRow> Depots { get; } = [];

    [ObservableProperty]
    private string _gameName = string.Empty;

    [ObservableProperty]
    private string _appId = string.Empty;

    [ObservableProperty]
    private string _archivePath = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _statusText = "Browse to a manifest .zip to begin.";

    [ObservableProperty]
    private bool _isBusy;

    private CancellationTokenSource? _downloadCts;
    private readonly Dictionary<string, string> _depotKeyMap = new();

    public DownloadsViewModel(
        ManifestArchiveService archiveService,
        LuaManifestParser luaParser,
        SteamDepotMetadataService depotMetadata,
        AppSettingsService settingsService,
        GameLibraryService gameLibrary,
        GameInstallPathService installPathService,
        PostDownloadService postDownload,
        AppMessageBoxService messageBoxService,
        FileExplorerPickerService filePicker,
        AppNotificationService notifications,
        IServiceProvider services)
    {
        _archiveService = archiveService;
        _luaParser = luaParser;
        _depotMetadata = depotMetadata;
        _settingsService = settingsService;
        _gameLibrary = gameLibrary;
        _installPathService = installPathService;
        _postDownload = postDownload;
        _messageBoxService = messageBoxService;
        _filePicker = filePicker;
        _notifications = notifications;
        _services = services;
    }


    [RelayCommand]
    private async Task BrowseArchiveAsync()
    {
        var paths = await _filePicker.PickFilesAsync([".zip"], "Select manifest archive");
        if (paths.Count == 0)
            return;
        await LoadArchiveAsync(paths[0]);
    }

    public async Task LoadArchiveAsync(string archivePath)
    {
        IsBusy = true;
        StatusText = "Extracting archive…";
        try
        {
            var result = await _archiveService.ExtractAsync(archivePath);
            ArchivePath = result.ExtractionDirectory;
            AppId = result.AppId;
            GameName = result.AppId;

            string? luaPath = ManifestArchiveService.FindLuaPath(result.AppId);
            if (string.IsNullOrWhiteSpace(luaPath))
            {
                StatusText = "No .lua manifest script found in the archive.";
                return;
            }
            var depots = _luaParser.Parse(luaPath);
            var onDisk = ManifestArchiveService.FindExtractionDirectory(result.AppId);
            Depots.Clear();
            _depotKeyMap.Clear();
            foreach (var depot in depots)
            {
                if (string.IsNullOrWhiteSpace(depot.HexKey))
                    continue;
                _depotKeyMap[depot.DepotId] = depot.HexKey;
                Depots.Add(new DepotRow
                {
                    DepotId = depot.DepotId,
                    ManifestId = depot.ManifestId,
                    ManifestPath = depot.ManifestPath,
                    HexKey = depot.HexKey,
                    DisplayName = $"Depot {depot.DepotId}",
                    IsSelected = false
                });
            }
            StatusText = Depots.Count > 0
                ? $"{Depots.Count} depot(s) with keys found. Select depots, then Download."
                : "No depot decryption keys were found in the .lua.";
            if (!string.IsNullOrWhiteSpace(result.CoverArtPath))
                GameName = Path.GetFileName(result.ExtractionDirectory);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"[Downloads] Failed to load archive '{archivePath}'");
            StatusText = $"Failed to load archive: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        var selected = Depots.Where(d => d.IsSelected).ToList();
        if (selected.Count == 0)
        {
            await _messageBoxService.ShowAsync("No depots selected",
                "Select at least one depot to download.");
            return;
        }
        if (string.IsNullOrWhiteSpace(AppId))
        {
            await _messageBoxService.ShowAsync("No manifest loaded",
                "Browse to a manifest .zip first.");
            return;
        }
        string installPath = await _installPathService.GetInstallDirectoryAsync(GameName, AppId, promptIfMissing: true);
        var depotInfos = selected.Select(row => new DepotInfo
        {
            DepotId = row.DepotId,
            ManifestId = row.ManifestId,
            ManifestPath = row.ManifestPath,
            HexKey = row.HexKey
        }).ToList();
        var keys = selected
            .Where(row => !string.IsNullOrWhiteSpace(row.HexKey))
            .ToDictionary(row => row.DepotId, row => Convert.FromHexString(row.HexKey));

        _downloadCts = new CancellationTokenSource();
        IsBusy = true;
        StatusText = "Downloading…";
        var progress = new Progress<DownloadProgress>(p =>
        {
            ProgressValue = p.Percentage;
        });
        var settings = await _settingsService.LoadAsync();
        try
        {
            await GameDownload.BatchEngineStart(
                depotInfos,
                keys,
                installPath,
                progress,
                waitIfPaused: _ => Task.CompletedTask,
                _downloadCts.Token,
                settings.CdnCellId,
                settings.MaxConcurrentChunks);

            await _gameLibrary.UpsertAsync(new GameEntry
            {
                AppId = AppId,
                Name = GameName,
                InstallPath = installPath,
                IsInstalled = true,
                InstalledDepots = selected.Select(row => new InstalledDepotRecord
                {
                    DepotId = row.DepotId,
                    ManifestId = row.ManifestId
                }).ToList()
            });
            ManifestInstallStateService.SaveSnapshots(AppId, depotInfos);
            _notifications.ShowInstallCompleted(GameName);
            StatusText = "Download complete";
            ProgressValue = 100;
            RefreshService.RequestRefresh();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Download cancelled";
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"[Downloads] Download failed for '{GameName}'");
            StatusText = $"Download failed: {ex.Message}";
            await _messageBoxService.ShowAsync("Download failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _downloadCts?.Cancel();
        StatusText = "Cancelling…";
    }
}
