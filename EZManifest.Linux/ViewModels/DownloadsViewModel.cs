using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EZManifest.Linux.Services;
using EZManifest.Linux.Views;
using EZManifest.Models;
using EZManifest.Services;

namespace EZManifest.Linux.ViewModels;

public partial class DepotRow : ObservableObject
{
    public string DepotId { get; init; } = string.Empty;
    public string ManifestId { get; init; } = string.Empty;
    public string HexKey { get; init; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public DepotDisplayInfo Display { get; init; } = new();

    public string DisplayName => Display.TypeLabel;
    public string SizeText => Display.DownloadText;

    [ObservableProperty]
    private bool _isSelected;
}

public partial class DownloadsViewModel : ObservableObject
{
    private readonly ManifestArchiveService _archiveService;
    private readonly LuaManifestParser _manifestParser;
    private readonly SteamDepotMetadataService _depotMetadata;
    private readonly SteamMetadataService _steamMetadata;
    private readonly AppSettingsService _settingsService;
    private readonly GameLibraryService _gameLibrary;
    private readonly GameInstallPathService _installPathService;
    private readonly PostDownloadService _postDownload;
    private readonly AppMessageBoxService _messageBoxService;
    private readonly FileExplorerPickerService _filePicker;
    private readonly AppNotificationService _notifications;
    private readonly SteamNonSteamShortcutService _steamShortcuts;

    public ObservableCollection<DownloadItem> Downloads { get; } = [];

    [ObservableProperty]
    private string? _depotBoxUrl = AppSettingsService.DefaultManifestSourceUrl;

    [ObservableProperty]
    private string _statusText = "Download a game manifest .zip from the DepotBox page, then pick it below to install.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _depotBoxHint;

    [ObservableProperty]
    private string? _depotBoxUrlError;

    private string _finalPath = string.Empty;
    private string _appId = string.Empty;
    private string _currentGameName = string.Empty;
    private string _currentLogoPath = string.Empty;
    private string _currentCoverArtPath = string.Empty;
    private int _importBusy;

    public DownloadsViewModel(
        ManifestArchiveService archiveService,
        LuaManifestParser manifestParser,
        SteamDepotMetadataService depotMetadata,
        SteamMetadataService steamMetadata,
        AppSettingsService settingsService,
        GameLibraryService gameLibrary,
        GameInstallPathService installPathService,
        PostDownloadService postDownload,
        AppMessageBoxService messageBoxService,
        FileExplorerPickerService filePicker,
        AppNotificationService notifications,
        SteamNonSteamShortcutService steamShortcuts)
    {
        _archiveService = archiveService;
        _manifestParser = manifestParser;
        _depotMetadata = depotMetadata;
        _steamMetadata = steamMetadata;
        _settingsService = settingsService;
        _gameLibrary = gameLibrary;
        _installPathService = installPathService;
        _postDownload = postDownload;
        _messageBoxService = messageBoxService;
        _filePicker = filePicker;
        _notifications = notifications;
        _steamShortcuts = steamShortcuts;
        _ = LoadSavedSourceAsync();
    }

    private async Task LoadSavedSourceAsync()
    {
        try
        {
            var settings = await _settingsService.LoadAsync();
            string preferred = AppSettingsService.UsesPreferredManifestSource(settings)
                ? settings.PreferredManifestSourceUrl
                : AppSettingsService.DefaultManifestSourceUrl;
            DepotBoxUrl = string.IsNullOrWhiteSpace(preferred)
                ? AppSettingsService.DefaultManifestSourceUrl
                : preferred;
        }
        catch
        {
        }
    }

    private bool ValidateDepotBoxUrl(string? text)
    {
        DepotBoxUrlError = null;
        if (AppSettingsService.TryNormalizeHttpUrl(text, out string url))
            return true;
        DepotBoxUrlError = "Enter a valid http(s) URL.";
        return false;
    }

    partial void OnDepotBoxUrlChanged(string? value) => ValidateDepotBoxUrl(value);

    [RelayCommand]
    private void OpenDepotBoxInBrowser()
    {
        if (!ValidateDepotBoxUrl(DepotBoxUrl))
            return;
        AppSettingsService.TryNormalizeHttpUrl(DepotBoxUrl, out string url);
        AppLog.Write($"[Downloads] Opening DepotBox in system browser: {url}");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = url,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            DepotBoxHint = $"Opened {url} in your browser. Download the game manifest .zip, then use \"Import manifest .zip\" below.";
        }
        catch (Exception ex)
        {
            DepotBoxHint = $"Could not open the browser: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveSourceAsync()
    {
        if (!ValidateDepotBoxUrl(DepotBoxUrl))
            return;
        if (!AppSettingsService.TryNormalizeHttpUrl(DepotBoxUrl, out string url))
            return;
        await _settingsService.UpdateAsync(settings =>
        {
            settings.PreferredManifestSourceUrl = url;
            settings.UsePreferredManifestSource = !string.Equals(
                url,
                AppSettingsService.DefaultManifestSourceUrl,
                StringComparison.OrdinalIgnoreCase);
        });
        _settingsService.NotifyManifestSourceChanged();
        DepotBoxHint = $"Manifest source saved: {url}";
    }

    [RelayCommand]
    private async Task BrowseArchiveAsync()
    {
        try
        {
        var paths = await _filePicker.PickFilesAsync([".zip"], "Select manifest archive");
        if (paths.Count == 0)
            return;
        await ImportManifestAsync(paths[0]);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "[Downloads] Browse failed");
        }
    }

    public async Task ImportManifestAsync(string path)
    {
        if (Interlocked.CompareExchange(ref _importBusy, 1, 0) != 0)
        {
            AppLog.Write($"[Downloads] Import ignored (already in progress): {path}");
            return;
        }

        IsBusy = true;
        StatusText = $"Importing {Path.GetFileName(path)}...";
        try
        {
            var archive = await _archiveService.ExtractAsync(path);
            _finalPath = archive.ExtractionDirectory;
            _appId = archive.AppId;
            _currentGameName = $"Steam App {_appId}";
            _currentLogoPath = archive.LogoPath;
            _currentCoverArtPath = archive.CoverArtPath;
            AppLog.Write(
                $"[Downloads] Imported appId={_appId} dir={_finalPath} lua={archive.LuaFilePath} " +
                $"logo={archive.LogoPath} cover={archive.CoverArtPath}");

            if (Downloads.Any(d => string.Equals(d.AppId, _appId, StringComparison.OrdinalIgnoreCase)))
            {
                string existingName = Downloads
                    .FirstOrDefault(d => string.Equals(d.AppId, _appId, StringComparison.OrdinalIgnoreCase))
                    ?.GameName ?? _currentGameName;
                await _messageBoxService.ShowAsync(
                    "Already downloading",
                    $"\"{existingName}\" is currently in the download process and cannot be added again.");
                return;
            }

            await _steamMetadata.DownloadArtworkAsync(_appId, archive.LogoPath, archive.CoverArtPath, archive.HeroPath, archive.IconPath);

            var existing = (await _gameLibrary.LoadAsync())
                .FirstOrDefault(g => string.Equals(g.AppId, _appId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                await AddSteamGameToLibraryAsync(_appId, archive.CoverArtPath, isInstalled: false);
            }
            else
            {
                try
                {
                    string? resolvedName = await _steamMetadata.GetGameNameAsync(_appId);
                    if (!string.IsNullOrWhiteSpace(resolvedName))
                        _currentGameName = resolvedName;
                }
                catch (Exception ex)
                {
                    AppLog.Write(ex, "Resolve game name for existing library title failed");
                }
            }

            await ManifestDepotIdChoiceAsync(archive.LuaFilePath);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Manifest import failed");
            StatusText = $"Import failed: {AppLog.GetRootMessage(ex)}";
        }
        finally
        {
            Interlocked.Exchange(ref _importBusy, 0);
            IsBusy = false;
        }
    }

    private async Task AddSteamGameToLibraryAsync(string appId, string coverArt, bool isInstalled)
    {
        string gameName;
        try
        {
            gameName = await _steamMetadata.GetGameNameAsync(appId);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Resolve game name failed");
            gameName = string.Empty;
        }

        _currentGameName = string.IsNullOrWhiteSpace(gameName) ? $"Steam App {appId}" : gameName;
        await _gameLibrary.UpsertAsync(new GameEntry
        {
            AppId = appId,
            Name = _currentGameName,
            Image = coverArt,
            StartLocation = string.Empty,
            InstallPath = string.Empty,
            IsInstalled = false
        });
        RefreshService.RequestRefresh();
    }

    private async Task ManifestDepotIdChoiceAsync(string luaFilePath)
    {
        var availableItems = _manifestParser.Parse(luaFilePath);
        var depotIds = availableItems.Select(item => item.DepotId).ToList();
        var relatedAppIds = _manifestParser.ParseRelatedAppIds(luaFilePath).ToList();

        var settings = await _settingsService.LoadAsync();
        bool showAllDepots = settings.ShowAllDepotIds;
        StatusText = showAllDepots ? "Loading every depot..." : "Selecting Windows depots from Steam...";

        var metadata = await _depotMetadata.GetDepotMetadataAsync(_appId, depotIds, relatedAppIds);

        var displayRows = BuildDepotDisplayRows(availableItems, metadata, showAllDepots);

        var gameRows = displayRows
            .Where(row => !row.Display.IsLanguage && !row.Display.IsDlc && !row.Display.IsShared)
            .ToList();
        var dlcRows = displayRows
            .Where(row => row.Display.IsDlc)
            .OrderBy(row => DlcGroupName(row.Display), StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => DlcLanguageRank(row.Display))
            .ThenBy(row => row.Display.TypeLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var languageRows = displayRows
            .Where(row => row.Display.IsLanguage && !row.Display.IsDlc)
            .OrderBy(row => IsEnglishLanguage(row.Display.LanguageCode) ? 0 : 1)
            .ThenBy(row => row.Display.TypeLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!showAllDepots)
        {
            gameRows = SteamDepotPlatformFilter.PreferHostArch(gameRows);
            dlcRows = SteamDepotPlatformFilter.PreferHostArch(dlcRows);
            languageRows = SteamDepotPlatformFilter.PreferHostArch(languageRows);
        }

        if (gameRows.Count == 0 && languageRows.Count > 0)
        {
            gameRows = RelabelLanguageAsGame(languageRows, autoSelect: !showAllDepots);
            languageRows = [];
        }

        var gameSelection = await ShowDepotSelectionAsync(
            "Select game files",
            BuildDepotHint(gameRows, showAllDepots, metadata.Count),
            gameRows,
            primaryText: dlcRows.Count > 0 || languageRows.Count > 0 ? "Next" : "Install Selected",
            closeText: "Cancel",
            showInstallOptions: true);
        if (gameSelection is null)
            return;

        var selectedItems = gameSelection.Selected;
        bool removeSteamDrm = gameSelection.RemoveSteamDrm;
        bool addToSteam = gameSelection.AddToSteam;

        if (selectedItems.Count == 0 && dlcRows.Count == 0)
            return;

        if (dlcRows.Count > 0)
        {
            var dlcSelection = await ShowDepotSelectionAsync(
                "Select DLC",
                showAllDepots
                    ? "Optional. Every DLC depot, all platforms. Nothing is pre-selected."
                    : "Optional. Language-specific DLC packs are listed under each DLC name.",
                dlcRows,
                primaryText: languageRows.Count > 0 ? "Next" : "Install Selected",
                closeText: "Skip");
            if (dlcSelection is not null)
                selectedItems.AddRange(dlcSelection.Selected);
        }

        if (languageRows.Count > 0)
        {
            var languageSelection = await ShowDepotSelectionAsync(
                "Select language",
                showAllDepots
                    ? "Optional. Every language depot, all platforms. Nothing is pre-selected."
                    : "Optional. Leave none selected to skip extra languages.",
                languageRows,
                primaryText: "Install Selected",
                closeText: "Skip");
            if (languageSelection is not null)
                selectedItems.AddRange(languageSelection.Selected);
        }

        if (selectedItems.Count == 0)
            return;

        await StartDownloadProcessAsync(selectedItems.Select(item => item.Depot).ToList(), removeSteamDrm, addToSteam);
        RefreshService.RequestRefresh();
    }

    private static string BuildDepotHint(
        IReadOnlyList<(DepotInfo Depot, DepotDisplayInfo Display)> gameRows,
        bool showAllDepots,
        int metadataCount)
    {
        if (showAllDepots)
            return "Every game depot from the manifest, all platforms. Nothing is pre-selected.";
        int windowsSelected = gameRows.Count(row => row.Display.AutoSelected);
        if (windowsSelected > 0)
            return $"Windows game files Steam would install ({windowsSelected}). You can change this.";
        return metadataCount == 0
            ? "Steam depot info unavailable. Select depots manually."
            : "No Windows depot match - select the game files you want.";
    }

    private sealed record DepotSelectionResult(
        List<(DepotInfo Depot, DepotDisplayInfo Display)> Selected,
        bool RemoveSteamDrm,
        bool AddToSteam);

    private async Task<DepotSelectionResult?> ShowDepotSelectionAsync(
        string title,
        string hint,
        IReadOnlyList<(DepotInfo Depot, DepotDisplayInfo Display)> rows,
        string primaryText,
        string closeText,
        bool showInstallOptions = false)
    {
        if (rows.Count == 0)
            return new DepotSelectionResult([], false, false);

        var rowsVm = rows.Select(row => new DepotRow
        {
            DepotId = row.Depot.DepotId,
            ManifestId = row.Depot.ManifestId,
            HexKey = row.Depot.HexKey,
            ManifestPath = row.Depot.ManifestPath,
            Display = row.Display,
            IsSelected = row.Display.AutoSelected
        }).ToList();

        var vm = new DepotSelectionViewModel(title, hint, rowsVm, primaryText, closeText, showInstallOptions);
        var page = new DepotSelectionDialog { DataContext = vm };
        var result = await _messageBoxService.ShowDialogAsync(page, primaryText, closeText);
        if (result != ContentDialogResult.Primary)
            return null;

        var selected = rowsVm.Where(r => r.IsSelected)
            .Select(r => (rows.First(row => row.Depot.DepotId == r.DepotId).Depot, r.Display))
            .ToList();
        return new DepotSelectionResult(selected, vm.RemoveSteamDrm, vm.AddToSteam);
    }

    private List<(DepotInfo Depot, DepotDisplayInfo Display)> BuildDepotDisplayRows(
        IReadOnlyList<DepotInfo> depots,
        IReadOnlyDictionary<string, DepotMetadata> metadata,
        bool showAllDepots = false)
    {
        var staged = new List<(DepotInfo Depot, DepotDisplayInfo Display, DepotMetadata? Meta)>();
        foreach (var depot in depots)
        {
            metadata.TryGetValue(depot.DepotId, out var meta);
            string? manifestPath = ResolveManifestPath(depot);
            bool hasManifest = manifestPath is not null;

            long? size = meta?.SizeBytes;
            long? download = meta?.DownloadBytes;
            if (hasManifest)
            {
                var local = TryReadLocalManifestSizes(depot);
                size ??= local.Size;
                download ??= local.Download;
            }
            download ??= size;

            string languageCode = FirstLanguageCode(meta?.Language, meta?.Name, meta?.Configuration) ?? string.Empty;
            staged.Add((depot, new DepotDisplayInfo
            {
                DepotId = depot.DepotId,
                ManifestId = depot.ManifestId,
                Configuration = meta?.Configuration ?? string.Empty,
                TypeLabel = FormatDepotTypeLabel(meta, depot.DepotId, _currentGameName, showAllDepots),
                DepotName = meta?.Name ?? string.Empty,
                SizeBytes = size,
                DownloadBytes = download,
                HasLocalManifest = hasManifest,
                IsDlc = meta?.IsDlc == true,
                IsShared = meta?.IsShared == true,
                IsLanguage = !string.IsNullOrWhiteSpace(languageCode),
                LanguageCode = languageCode,
                OsArch = meta?.OsArch
            }, meta));
        }

        if (!showAllDepots)
        {
            staged = staged
                .Where(row => !SteamDepotPlatformFilter.IsMacOsOrLinuxOnly(row.Meta, row.Display))
                .ToList();
        }

        var windowsIds = showAllDepots
            ? []
            : SteamDepotPlatformFilter.SelectWindowsDepotIds(staged);
        AppLog.Write(
            showAllDepots
                ? $"[Downloads] Show all depot IDs ({staged.Count}), skip Windows auto-select"
                : $"[Downloads] Windows auto-select {windowsIds.Count}/{staged.Count} depot(s): " +
                  string.Join(", ", windowsIds));

        return staged
            .OrderBy(row => SteamDepotPlatformFilter.ListRank(row.Meta, row.Display))
            .ThenBy(row => string.IsNullOrWhiteSpace(row.Meta?.Language) ? 0 : 1)
            .ThenBy(row => row.Meta?.IsDlc == true ? 1 : 0)
            .ThenBy(row => row.Depot.DepotId, StringComparer.Ordinal)
            .Select(row =>
            {
                bool isEnglish = IsEnglishLanguage(row.Display.LanguageCode);
                var display = row.Display with
                {
                    AutoSelected = !showAllDepots && row.Display.HasLocalManifest && (
                        row.Display.IsDlc
                        || (row.Display.IsLanguage && isEnglish)
                        || (!row.Display.IsLanguage && windowsIds.Contains(row.Depot.DepotId)))
                };
                return (row.Depot, display);
            })
            .ToList();
    }

    private static string FormatDepotTypeLabel(
        DepotMetadata? meta,
        string depotId,
        string gameName,
        bool includePlatform = false)
    {
        string? language = FormatSteamLanguage(FirstLanguageCode(meta?.Language, meta?.Name));
        string label;
        if (meta?.IsDlc == true)
        {
            string dlcName = FormatDlcName(meta, depotId, gameName);
            label = string.IsNullOrWhiteSpace(language) ? dlcName : $"{dlcName} - {language}";
        }
        else if (!string.IsNullOrWhiteSpace(language))
        {
            label = $"Language: {language}";
        }
        else
        {
            label = meta?.TypeLabel ?? "Game";
        }

        if (includePlatform && !string.IsNullOrWhiteSpace(meta?.OsList))
            label = $"{label} - {meta.OsList}";

        return label;
    }

    [GeneratedRegex(@"\s+-\s+(?:Content|[a-z]{2}(?:_[A-Za-z]{2})?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LocaleSuffixRegex();

    private static string FormatDlcName(DepotMetadata meta, string depotId, string gameName)
    {
        string original = meta.Name?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(meta.DlcAppId)
            && original.Equals($"DLC {meta.DlcAppId}", StringComparison.OrdinalIgnoreCase))
        {
            original = string.Empty;
        }

        string name = original;
        if (!string.IsNullOrWhiteSpace(name))
        {
            int paren = name.IndexOf(" (", StringComparison.Ordinal);
            if (paren >= 0)
                name = name[..paren];

            name = LocaleSuffixRegex().Replace(name, string.Empty).Trim();
            foreach (string suffix in new[] { " Depot", " depot", " デポ" })
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    name = name[..^suffix.Length].Trim();
            }

            if (!string.IsNullOrWhiteSpace(gameName)
                && name.StartsWith(gameName, StringComparison.OrdinalIgnoreCase))
            {
                string stripped = name[gameName.Length..].TrimStart(' ', '-', ':');
                name = string.IsNullOrWhiteSpace(stripped) ? name : stripped;
            }
            else
            {
                int dash = name.IndexOf(" - ", StringComparison.Ordinal);
                if (dash >= 0)
                {
                    string afterDash = name[(dash + 3)..].Trim();
                    if (!string.IsNullOrWhiteSpace(afterDash))
                        name = afterDash;
                }
            }

            if (!string.IsNullOrWhiteSpace(name)
                && !name.Equals(gameName, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }

            if (!string.IsNullOrWhiteSpace(original)
                && !original.Equals(gameName, StringComparison.OrdinalIgnoreCase))
            {
                return original;
            }
        }

        return $"DLC {meta.DlcAppId ?? depotId}";
    }

    private static string? FirstLanguageCode(params string?[] values)
    {
        string? steamLanguage = values.Length > 0 ? values[0] : null;
        if (!string.IsNullOrWhiteSpace(steamLanguage))
            return steamLanguage.Trim();

        for (int i = 1; i < values.Length; i++)
        {
            string? inferred = SteamLanguageNames.InferFromName(values[i]);
            if (!string.IsNullOrWhiteSpace(inferred))
                return inferred;
        }

        return null;
    }

    private static bool IsEnglishLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;
        string normalized = code.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized is "english" or "en" or "en_us" or "en_gb";
    }

    private static string DlcGroupName(DepotDisplayInfo display)
    {
        string label = display.TypeLabel;
        int separator = label.LastIndexOf(" — ", StringComparison.Ordinal);
        return separator >= 0 ? label[..separator] : label;
    }

    private static int DlcLanguageRank(DepotDisplayInfo display) =>
        !display.IsLanguage ? 0 : IsEnglishLanguage(display.LanguageCode) ? 1 : 2;

    private static string? FormatSteamLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        return code.Trim().ToLowerInvariant() switch
        {
            "arabic" => "Arabic",
            "brazilian" => "Portuguese - Brazil",
            "bulgarian" => "Bulgarian",
            "czech" => "Czech",
            "danish" => "Danish",
            "dutch" => "Dutch",
            "english" => "English",
            "finnish" => "Finnish",
            "french" => "French",
            "german" => "German",
            "greek" => "Greek",
            "hungarian" => "Hungarian",
            "indonesian" => "Indonesian",
            "italian" => "Italian",
            "japanese" => "Japanese",
            "koreana" => "Korean",
            "latam" => "Spanish - Latin America",
            "norwegian" => "Norwegian",
            "polish" => "Polish",
            "portuguese" => "Portuguese",
            "romanian" => "Romanian",
            "russian" => "Russian",
            "schinese" => "Chinese - Simplified",
            "spanish" => "Spanish",
            "swedish" => "Swedish",
            "thai" => "Thai",
            "tchinese" => "Chinese - Traditional",
            "turkish" => "Turkish",
            "ukrainian" => "Ukrainian",
            "vietnamese" => "Vietnamese",
            _ => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(code.Replace('_', ' '))
        };
    }

    private static List<(DepotInfo Depot, DepotDisplayInfo Display)> RelabelLanguageAsGame(
        IReadOnlyList<(DepotInfo Depot, DepotDisplayInfo Display)> languageRows,
        bool autoSelect = true)
    {
        var result = languageRows
            .Select(row =>
            {
                string? language = FormatSteamLanguage(row.Display.LanguageCode);
                return (row.Depot, Display: row.Display with
                {
                    TypeLabel = string.IsNullOrWhiteSpace(language) ? "Game" : $"Game - {language}"
                });
            })
            .ToList();

        if (autoSelect && result.Count > 0 && !result.Any(row => row.Display.AutoSelected))
        {
            var first = result[0];
            result[0] = (first.Depot, first.Display with { AutoSelected = first.Display.HasLocalManifest });
        }

        return result;
    }

    private string? ResolveManifestPath(DepotInfo depot)
    {
        if (!string.IsNullOrWhiteSpace(depot.ManifestPath) && File.Exists(depot.ManifestPath))
            return depot.ManifestPath;
        if (string.IsNullOrWhiteSpace(_finalPath))
            return null;
        string fallback = Path.Combine(_finalPath, $"{depot.DepotId}_{depot.ManifestId}.manifest");
        return File.Exists(fallback) ? fallback : null;
    }

    private (long? Size, long? Download) TryReadLocalManifestSizes(DepotInfo depot)
    {
        try
        {
            string? manifestPath = ResolveManifestPath(depot);
            if (manifestPath is null)
                return (null, null);
            byte[] data = File.ReadAllBytes(manifestPath);
            var manifest = SteamKit2.DepotManifest.Deserialize(data);
            if (manifest.Files is null)
                return (null, null);
            long size = 0;
            long download = 0;
            foreach (var file in manifest.Files)
            {
                foreach (var chunk in file.Chunks)
                {
                    size += chunk.UncompressedLength;
                    download += chunk.CompressedLength;
                }
            }
            return (size, download);
        }
        catch
        {
            return (null, null);
        }
    }

    private async Task<bool> HasEnoughDiskSpaceAsync(IReadOnlyList<DepotInfo> selectedDepots)
    {
        string installDirectory = await _installPathService.GetInstallDirectoryAsync(_currentGameName, _appId, promptIfMissing: false);
        long required = await EstimateSelectedInstallBytesAsync(selectedDepots);
        if (required <= 0)
            return true;

        if (!DriveSpace.TryGetAvailableBytes(installDirectory, out long available))
            return true;

        long needed = required + DriveSpace.SafetyReserveBytes;
        if (available >= needed)
            return true;

        string drive = DriveSpace.DriveName(installDirectory);
        await _messageBoxService.ShowAsync(
            "Not enough space",
            $"\"{_currentGameName}\" needs {DriveSpace.FormatBytes(required)} to install on {drive}, " +
            $"but only {DriveSpace.FormatBytes(available)} is free.\n\n" +
            "Free up space or change the download folder in Settings, then try again.");
        return false;
    }

    private async Task<long> EstimateSelectedInstallBytesAsync(IReadOnlyList<DepotInfo> selectedDepots)
    {
        var metadata = string.IsNullOrWhiteSpace(_appId)
            ? new Dictionary<string, DepotMetadata>()
            : await _depotMetadata.GetDepotMetadataAsync(_appId, selectedDepots.Select(depot => depot.DepotId));

        long total = 0;
        foreach (var depot in selectedDepots)
        {
            long? size = null;
            if (metadata.TryGetValue(depot.DepotId, out var meta))
                size = meta.SizeBytes;

            if (size is not > 0)
                size = TryReadLocalManifestSizes(depot).Size;

            if (size is > 0)
                total += size.Value;
        }

        return total;
    }

    private async Task StartDownloadProcessAsync(
        List<DepotInfo> selectedDepots,
        bool executePostDownload,
        bool addToSteam)
    {
        if (Downloads.Any(d => string.Equals(d.AppId, _appId, StringComparison.OrdinalIgnoreCase)))
        {
            await _messageBoxService.ShowAsync(
                "Already downloading",
                $"\"{_currentGameName}\" is currently in the download process and cannot be added again.");
            return;
        }

        if (await _installPathService.TryEnsureDownloadRootAsync() is null)
        {
            await _messageBoxService.ShowAsync(
                "Install location required",
                "Choose a default install folder before downloading.");
            return;
        }

        if (!await HasEnoughDiskSpaceAsync(selectedDepots))
            return;

        var cancellation = new CancellationTokenSource();
        var pause = new DownloadPauseState();
        var downloadItem = new DownloadItem
        {
            GameName = _currentGameName,
            AppId = _appId,
            Status = $"Preparing {selectedDepots.Count} depot(s)...",
            ProgressValue = 0
        };

        bool cancelRequested = false;
        downloadItem.CancelCommand = new RelayCommand(() =>
        {
            if (cancelRequested)
                return;
            cancelRequested = true;
            downloadItem.Status = "Cancelling...";
            try
            {
                if (!cancellation.IsCancellationRequested)
                    cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        });
        downloadItem.PauseCommand = new RelayCommand(() =>
        {
            if (cancelRequested)
                return;
            if (pause.Toggle())
            {
                downloadItem.PauseElapsed();
                downloadItem.Status = "Paused";
                downloadItem.PauseButtonText = "Resume";
            }
            else
            {
                downloadItem.ResumeElapsed();
                downloadItem.Status = "Downloading game files...";
                downloadItem.PauseButtonText = "Pause";
            }
        });

        Downloads.Add(downloadItem);
        downloadItem.StartElapsed();
        AppLog.Write(
            $"[Downloads] Queued download '{_currentGameName}' appId={_appId} " +
            $"selectedDepots={selectedDepots.Count} executePostDownload={executePostDownload} addToSteam={addToSteam}");

        var progressReporter = new Progress<DownloadProgress>(progress =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (cancelRequested)
                    return;
                downloadItem.DownloadedBytes = progress.DownloadedBytes;
                downloadItem.TotalBytes = progress.TotalBytes;
                downloadItem.NetworkBytesReceived = progress.NetworkBytesReceived;
                downloadItem.ProgressValue = progress.Percentage;
                if (progress.DownloadedBytes > 0 || progress.NetworkBytesReceived > 0)
                    downloadItem.Status = "Downloading game files...";
            });
        });

        _ = Task.Run(async () =>
        {
            bool downloadCompleted = false;
            string? downloadDest = null;
            string appId = _appId;
            string gameName = _currentGameName;
            string coverArt = _currentCoverArtPath;
            try
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    downloadItem.Status = "Validating depots and keys...");

                var depotKeys = new Dictionary<string, byte[]>();
                var readyDepots = new List<DepotInfo>();
                var skippedDepots = new List<string>();

                foreach (var depot in selectedDepots)
                {
                    cancellation.Token.ThrowIfCancellationRequested();

                    string? manifestPath = ResolveManifestPath(depot);
                    if (manifestPath is null)
                    {
                        skippedDepots.Add(depot.DepotId);
                        AppLog.Write($"[Downloads] Depot {depot.DepotId}: missing local .manifest (skipped)");
                        continue;
                    }

                    if (string.IsNullOrEmpty(depot.HexKey))
                        throw new Exception($"Missing HexKey for Depot {depot.DepotId}");

                    depotKeys[depot.DepotId] = Convert.FromHexString(depot.HexKey);
                    depot.ManifestPath = manifestPath;
                    readyDepots.Add(depot);
                }

                if (readyDepots.Count == 0)
                    throw new Exception("No selected depots have a local .manifest file to download.");

                if (skippedDepots.Count > 0)
                    AppLog.Write($"[Downloads] Skipped depots without manifest: {string.Join(", ", skippedDepots)}");

                downloadDest = await _installPathService.GetInstallDirectoryAsync(gameName, appId, promptIfMissing: false);
                Directory.CreateDirectory(downloadDest);
                await _gameLibrary.UpsertAsync(new GameEntry
                {
                    AppId = appId,
                    Name = gameName,
                    Image = coverArt,
                    InstallPath = downloadDest,
                    IsInstalled = false
                });

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    downloadItem.Status = "Preparing install files...");

                int cdnCellId = await _settingsService.GetCdnCellIdAsync();
                int maxConcurrentChunks = await _settingsService.GetMaxConcurrentChunksAsync();
                await GameDownload.BatchEngineStart(
                    readyDepots,
                    depotKeys,
                    downloadDest,
                    progressReporter,
                    pause.WaitWhilePausedAsync,
                    cancellation.Token,
                    cdnCellId,
                    maxConcurrentChunks);
                downloadCompleted = true;
                AppLog.Write($"[Downloads] Completed '{gameName}' -> {downloadDest}");

                var games = await _gameLibrary.LoadAsync();
                GameEntry? existing = games.FirstOrDefault(item =>
                    string.Equals(item.AppId, appId, StringComparison.OrdinalIgnoreCase));
                var installedDepots = (existing?.InstalledDepots ?? [])
                    .Where(record => !string.IsNullOrWhiteSpace(record.DepotId))
                    .GroupBy(record => record.DepotId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                foreach (DepotInfo depot in readyDepots)
                {
                    installedDepots[depot.DepotId] = new InstalledDepotRecord
                    {
                        DepotId = depot.DepotId,
                        ManifestId = depot.ManifestId
                    };
                }

                var savedDepots = installedDepots.Values.ToList();
                await _gameLibrary.UpsertAsync(new GameEntry
                {
                    AppId = appId,
                    Name = gameName,
                    Image = coverArt,
                    InstallPath = downloadDest,
                    IsInstalled = true,
                    InstalledDepots = savedDepots
                });
                ManifestInstallStateService.SaveSnapshots(appId, readyDepots);
                RefreshService.RequestRefresh();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _notifications.ShowInstallCompleted(gameName));

                if (executePostDownload)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        downloadItem.Status = "Removing Steam DRM - SteamAutoCrack in progress...");
                    await _postDownload.RunPostDownloadCommandAsync(gameName, appId, downloadDest, cancellation.Token);
                }

                if (addToSteam && !string.IsNullOrWhiteSpace(downloadDest))
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        downloadItem.Status = "Adding game to Steam...");
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        AddInstalledGameToSteamAsync(gameName, appId, downloadDest, coverArt));
                }

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    downloadItem.ProgressValue = 100;
                    downloadItem.Status = "Download complete";
                    Downloads.Remove(downloadItem);
                    StatusText = $"Download complete: {gameName}";
                });
            }
            catch (Exception ex) when (ex is OperationCanceledException
                || (ex.InnerException is OperationCanceledException)
                || cancellation.IsCancellationRequested)
            {
                AppLog.Write($"[Downloads] Cancelled '{gameName}' appId={appId}");
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    downloadItem.Status = "Cancelled";
                    Downloads.Remove(downloadItem);
                    StatusText = $"Download cancelled: {gameName}";
                });
            }
            catch (Exception ex)
            {
                string rootMessage = AppLog.GetRootMessage(ex);
                AppLog.Write(ex, "Download critical error");
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    if (downloadCompleted)
                    {
                        StatusText = $"Installed with issues: {gameName} ({rootMessage})";
                        Downloads.Remove(downloadItem);
                        return;
                    }
                    downloadItem.Status = $"Failed: {rootMessage}";
                    Downloads.Remove(downloadItem);
                    StatusText = $"Download failed: {rootMessage}";
                    await _messageBoxService.ShowAsync("Download failed", rootMessage);
                });
            }
            finally
            {
                cancellation.Dispose();
            }
        });
    }

    private async Task AddInstalledGameToSteamAsync(
        string gameName,
        string appId,
        string installFolder,
        string? coverArt)
    {
        try
        {
            var games = await _gameLibrary.LoadAsync();
            GameEntry game = games.FirstOrDefault(item =>
                    string.Equals(item.AppId, appId, StringComparison.OrdinalIgnoreCase))
                ?? new GameEntry
                {
                    AppId = appId,
                    Name = gameName,
                    Image = coverArt,
                    InstallPath = installFolder,
                    IsInstalled = true
                };
            if (string.IsNullOrWhiteSpace(game.InstallPath))
                game.InstallPath = installFolder;

            string? exePath = await PickGameExecutableAsync(game, installFolder);
            if (string.IsNullOrWhiteSpace(exePath))
            {
                await _messageBoxService.ShowAsync(
                    "Not added to Steam",
                    "Choose the game executable to add it as a non-Steam game.");
                return;
            }
            game.StartLocation = exePath;
            await _gameLibrary.UpsertAsync(game);
            SteamShortcutAddResult result = await _steamShortcuts.AddToAllAccountsAsync(game, exePath);
            string message = $"{game.Name} has been added as a non-Steam game to {result.AccountsUpdated} account(s).";
            if (!result.SteamWasRunning)
            {
                await _messageBoxService.ShowAsync("Game has been added to Steam", message);
                return;
            }
            ContentDialogResult restart = await _messageBoxService.ShowAsync(
                "Game has been added to Steam",
                message + "\n\nWould you like to restart Steam for the changes to take effect?",
                "Restart Steam",
                "Not now");
            if (restart != ContentDialogResult.Primary)
                return;
            await _steamShortcuts.RestartSteamAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Failed to add '{gameName}' to Steam after install");
            await _messageBoxService.ShowAsync("Could not add game to Steam", ex.Message);
        }
    }

    private async Task<string?> PickGameExecutableAsync(GameEntry game, string gameFolder)
    {
        if (string.IsNullOrWhiteSpace(gameFolder) || !await Task.Run(() => Directory.Exists(gameFolder)))
        {
            await _messageBoxService.ShowAsync(
                "Game not installed",
                $"Could not find the install folder for {game.Name}.");
            return null;
        }
        var executables = await Task.Run(() =>
            Directory.EnumerateFiles(gameFolder, "*.exe", SearchOption.AllDirectories)
                .OrderBy(path => Path.GetRelativePath(gameFolder, path).Count(c => c is '\\' or '/'))
                .ThenBy(path => Path.GetRelativePath(gameFolder, path), StringComparer.OrdinalIgnoreCase)
                .ToList());
        if (executables.Count == 0)
        {
            await _messageBoxService.ShowAsync(
                "No executables found",
                $"No .exe files were found in:\n{gameFolder}");
            return null;
        }

        var vm = new ExeSelectionViewModel(
            $"Choose the game executable for {game.Name}",
            executables.Select(path =>
                Path.GetRelativePath(gameFolder, path)).ToList());
        var page = new ExeSelectionDialog { DataContext = vm };
        var result = await _messageBoxService.ShowDialogAsync(page, "Add to Steam", "Cancel");
        if (result != ContentDialogResult.Primary || vm.SelectedIndex < 0 || vm.SelectedIndex >= executables.Count)
            return null;
        return executables[vm.SelectedIndex];
    }

    private sealed class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }
}
