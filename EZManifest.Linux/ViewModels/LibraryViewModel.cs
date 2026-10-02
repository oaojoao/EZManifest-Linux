using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EZManifest.Linux.Services;
using EZManifest.Linux.Views;
using EZManifest.Models;
using EZManifest.Services;

namespace EZManifest.Linux.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly GameLibraryService _gameLibrary;
    private readonly GameLauncher _gameLauncher;
    private readonly GameUninstallService _uninstallService;
    private readonly GameInstallSizeService _installSizeService;
    private readonly SteamMetadataService _steamMetadata;
    private readonly AppMessageBoxService _messageBoxService;
    private readonly FileExplorerPickerService _filePicker;

    public ObservableCollection<GameEntry> FilteredApps { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private GameEntry? _selectedGame;

    [ObservableProperty]
    private string _selectedGameStatus = string.Empty;

    [ObservableProperty]
    private string? _selectedGameHeroPath;

    [ObservableProperty]
    private string? _selectedGameLogoPath;

    [ObservableProperty]
    private ObservableCollection<GameMediaItem> _selectedGameMedia = [];

    private List<GameEntry> _allGames = [];

    public LibraryViewModel(
        GameLibraryService gameLibrary,
        GameLauncher gameLauncher,
        GameUninstallService uninstallService,
        GameInstallSizeService installSizeService,
        SteamMetadataService steamMetadata,
        AppMessageBoxService messageBoxService,
        FileExplorerPickerService filePicker)
    {
        _gameLibrary = gameLibrary;
        _gameLauncher = gameLauncher;
        _uninstallService = uninstallService;
        _installSizeService = installSizeService;
        _steamMetadata = steamMetadata;
        _messageBoxService = messageBoxService;
        _filePicker = filePicker;
        UrlToBitmapConverter.HttpImageLoaded += OnHttpImageLoaded;
        _ = RefreshAsync();
    }

    private void OnHttpImageLoaded(string url)
    {
        var items = SelectedGameMedia.Where(i => i.ThumbnailUrl == url).ToList();
        foreach (var item in items)
        {
            int index = SelectedGameMedia.IndexOf(item);
            if (index >= 0)
            {
                SelectedGameMedia[index] = item;
                break;
            }
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        string query = (SearchText ?? string.Empty).Trim();
        var source = string.IsNullOrWhiteSpace(query)
            ? _allGames
            : _allGames.Where(g => g.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        FilteredApps.Clear();
        foreach (var game in source)
            FilteredApps.Add(game);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
        var games = await _gameLibrary.LoadAsync();
        _allGames = games;
        ApplyFilter();
        StatusText = $"{FilteredApps.Count} game(s)";
        foreach (var game in _allGames.Where(g => g.IsInstalled))
        {
            _ = ResolveInstallSizeAsync(game);
        }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "[Library] Refresh failed");
            StatusText = "Could not load the library.";
        }
    }

    private async Task ResolveInstallSizeAsync(GameEntry game)
    {
        try
        {
            long? size = await _installSizeService.ResolveAsync(game);
            if (size is > 0)
                game.InstallSizeBytes = size;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"[Library] Install size failed for '{game.Name}'");
        }
    }

    [RelayCommand]
    private void SelectGame(GameEntry game)
    {
        if (game is null)
            return;
        SelectedGame = game;
        SelectedGameStatus = game.IsInstalled ? "Installed" : game.IsInstalling ? "Installing" : "Not installed";
    }

    private async Task LoadDetailAsync(GameEntry game)
    {
        SelectedGameHeroPath = null;
        SelectedGameLogoPath = null;
        SelectedGameMedia.Clear();

        string? heroPath = SteamMetadataService.ResolveHeroPath(game.Image);
        if (string.IsNullOrWhiteSpace(heroPath) && !string.IsNullOrWhiteSpace(game.AppId))
            heroPath = Path.Combine(AppPaths.ManifestsDirectory, $"undefined_{game.AppId}", "Assets", "LibraryHero.jpg");
        if (!string.IsNullOrWhiteSpace(heroPath)
            && (!File.Exists(heroPath) || new FileInfo(heroPath).Length == 0)
            && !string.IsNullOrWhiteSpace(game.AppId))
        {
            try
            {
                await _steamMetadata.DownloadHeroAsync(game.AppId, heroPath);
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, $"[Library] Hero download failed for appId={game.AppId}");
            }
        }
        if (!string.IsNullOrWhiteSpace(heroPath) && File.Exists(heroPath) && new FileInfo(heroPath).Length > 0)
            SelectedGameHeroPath = heroPath;

        if (!string.IsNullOrWhiteSpace(game.Image))
        {
            string? directory = Path.GetDirectoryName(game.Image);
            string logoPath = directory is null ? null : Path.Combine(directory, "GameLogo.png");
            if (logoPath is not null && File.Exists(logoPath))
                SelectedGameLogoPath = logoPath;
        }

        if (string.IsNullOrWhiteSpace(game.AboutTheGame) && !game.AboutTheGameLoaded
            && !string.IsNullOrWhiteSpace(game.AppId))
        {
            try
            {
                SteamStorePageInfo info = await _steamMetadata.GetStorePageInfoAsync(game.AppId);
                if (!string.IsNullOrWhiteSpace(info.AboutTheGame))
                {
                    game.AboutTheGame = info.AboutTheGame;
                    await _gameLibrary.SaveAsync(_allGames);
                }
                game.AboutTheGameLoaded = true;
                game.SetMedia(info.Media);
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, $"[Library] Store details failed for appId={game.AppId}");
                game.AboutTheGameLoaded = true;
                game.SetMedia([]);
            }
        }

        SelectedGameMedia.Clear();
        if (game.MediaLoaded)
        {
            foreach (var item in game.MediaItems.Take(6))
                SelectedGameMedia.Add(item);
        }
        OnPropertyChanged(nameof(SelectedGame));
    }

    [RelayCommand]
    private async Task PlayAsync(GameEntry game)
    {
        if (game is null)
            return;
        try
        {
            string startLocation = game.StartLocation;
            bool needsPick = string.IsNullOrWhiteSpace(startLocation) || !File.Exists(startLocation);
            if (needsPick)
            {
                if (!string.IsNullOrWhiteSpace(startLocation))
                    StatusText = $"Saved executable was not found, picking a new one for {game.Name}...";
                string? picked = await PickGameExecutableAsync(game);
                if (string.IsNullOrWhiteSpace(picked))
                    return;
                startLocation = picked;
                game.StartLocation = startLocation;
                await _gameLibrary.SaveAsync(_allGames);
            }
            string exePath = Path.GetFullPath(startLocation);
            string? workingDirectory = Path.GetDirectoryName(exePath);
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                await _messageBoxService.ShowAsync("Unable to start",
                    $"Could not resolve working directory for:\n{exePath}");
                return;
            }
            await Task.Run(() => _gameLauncher.LaunchAsync(game, exePath, workingDirectory));
            game.IsRunning = true;
            SelectedGameStatus = "Running";
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Unable to start game '{game.Name}'");
            await _messageBoxService.ShowAsync("Unable to start", ex.Message);
        }
    }

    [RelayCommand]
    private void StopGame(GameEntry game)
    {
        game.IsRunning = false;
        SelectedGameStatus = game.IsInstalled ? "Installed" : "Not installed";
    }

    [RelayCommand]
    private async Task UninstallAsync(GameEntry game)
    {
        if (game is null)
            return;
        try
        {
            var result = await _messageBoxService.ShowAsync(
                "Uninstall game",
                $"Remove {game.Name} and delete its install folder?",
                "Uninstall",
                "Cancel");
            if (result != ContentDialogResult.Primary)
                return;
            SelectedGame = null;
            await _uninstallService.UninstallAsync(game);
            await RefreshAsync();
            StatusText = $"{game.Name} was uninstalled";
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Uninstall failed for '{game.Name}'");
            await _messageBoxService.ShowAsync("Uninstall failed", ex.Message);
        }
    }

    partial void OnSelectedGameChanged(GameEntry? value)
    {
        OnPropertyChanged(nameof(HasSelectedGame));
        if (value is not null)
            _ = LoadDetailAsync(value);
    }

    public bool HasSelectedGame => SelectedGame is not null;

    [RelayCommand]
    private async Task SaveGamePrefixAsync(GameEntry game)
    {
        if (game is null)
            return;
        try
        {
            await _gameLibrary.SaveAsync(_allGames);
            StatusText = $"WINE prefix for {game.Name} saved";
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"[Library] Could not save prefix for '{game.Name}'");
        }
    }

    [RelayCommand]
    private async Task BrowseGamePrefixAsync(GameEntry game)
    {
        if (game is null)
            return;
        string? folder = await _filePicker.PickFolderAsync($"Select WINE prefix for {game.Name}");
        if (string.IsNullOrWhiteSpace(folder))
            return;
        game.ProtonPrefixPath = folder;
        await _gameLibrary.SaveAsync(_allGames);
    }

    [RelayCommand]
    private void OpenFolder(GameEntry game)
    {
        if (game is null || string.IsNullOrWhiteSpace(game.InstallPath) || !Directory.Exists(game.InstallPath))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = game.InstallPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write($"[Library] Could not open folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenStorePage(GameEntry game)
    {
        if (game is null || string.IsNullOrWhiteSpace(game.AppId))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = $"https://store.steampowered.com/app/{game.AppId}/",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write($"[Library] Could not open store page: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenMedia(GameMediaItem item)
    {
        if (item is null)
            return;
        string? url = item.IsVideo ? item.VideoUrl : item.ImageUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = url,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write($"[Library] Could not open media: {ex.Message}");
        }
    }

    private async Task<string?> PickGameExecutableAsync(GameEntry game)
    {
        string? folder = game.InstallPath;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            await _messageBoxService.ShowAsync("Game not installed",
                $"Could not find the install folder for {game.Name}.");
            return null;
        }
        List<string> executables = await Task.Run(() =>
            Directory.EnumerateFiles(folder, "*.exe", SearchOption.AllDirectories)
                .OrderBy(path => Path.GetRelativePath(folder, path).Count(c => c is '\\' or '/'))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList());
        if (executables.Count == 0)
        {
            await _messageBoxService.ShowAsync("No executable found",
                "No .exe file was found in the game folder.");
            return null;
        }
        if (executables.Count == 1)
            return executables[0];

        var vm = new ExeSelectionViewModel(
            $"Choose the game executable for {game.Name}",
            executables.Select(path => Path.GetRelativePath(folder, path)).ToList())
        {
            AllowBrowse = true
        };
        var page = new ExeSelectionDialog { DataContext = vm };
        var result = await _messageBoxService.ShowDialogAsync(page, "Play", "Cancel");
        if (result != ContentDialogResult.Primary)
            return null;
        if (vm.SelectedIndex >= 0 && vm.SelectedIndex < executables.Count)
            return executables[vm.SelectedIndex];
        if (vm.BrowseRequested)
        {
            var paths = await _filePicker.PickFilesAsync(
                [".exe"],
                $"Select executable for {game.Name}",
                folder,
                allowMultiSelect: false);
            return paths.Count > 0 ? paths[0] : null;
        }
        return null;
    }
}
