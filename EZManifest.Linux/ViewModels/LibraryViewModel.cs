using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EZManifest.Linux.Services;
using EZManifest.Models;
using EZManifest.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EZManifest.Linux.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly GameLibraryService _gameLibrary;
    private readonly GameLauncher _gameLauncher;
    private readonly GameUninstallService _uninstallService;
    private readonly AppMessageBoxService _messageBoxService;
    private readonly FileExplorerPickerService _filePicker;
    private readonly IServiceProvider _services;

    public ObservableCollection<GameEntry> Games { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public LibraryViewModel(
        GameLibraryService gameLibrary,
        GameLauncher gameLauncher,
        GameUninstallService uninstallService,
        AppMessageBoxService messageBoxService,
        FileExplorerPickerService filePicker,
        IServiceProvider services)
    {
        _gameLibrary = gameLibrary;
        _gameLauncher = gameLauncher;
        _uninstallService = uninstallService;
        _messageBoxService = messageBoxService;
        _filePicker = filePicker;
        _services = services;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var games = await _gameLibrary.LoadAsync();
        Games.Clear();
        foreach (var game in games)
        {
            Games.Add(game);
        }
        StatusText = $"{Games.Count} game(s)";
    }

    [RelayCommand]
    private async Task PlayAsync(GameEntry game)
    {
        try
        {
            string startLocation = game.StartLocation;
            if (string.IsNullOrWhiteSpace(startLocation))
            {
                string? picked = await PickGameExecutableAsync(game);
                if (string.IsNullOrWhiteSpace(picked))
                    return;
                startLocation = picked;
                game.StartLocation = startLocation;
                await _gameLibrary.SaveAsync(Games);
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
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Unable to start game '{game.Name}'");
            await _messageBoxService.ShowAsync("Unable to start", ex.Message);
        }
    }

    [RelayCommand]
    private async Task UninstallAsync(GameEntry game)
    {
        var result = await _messageBoxService.ShowAsync(
            "Uninstall game",
            $"Remove {game.Name} and delete its install folder?",
            "Uninstall",
            "Cancel");
        if (result != ContentDialogResult.Primary)
            return;
        try
        {
            await _uninstallService.UninstallAsync(game);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Uninstall failed for '{game.Name}'");
            await _messageBoxService.ShowAsync("Uninstall failed", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenFolder(GameEntry game)
    {
        if (string.IsNullOrWhiteSpace(game.InstallPath) || !Directory.Exists(game.InstallPath))
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
        var paths = await _filePicker.PickFilesAsync(
            [".exe"],
            $"Select executable for {game.Name}",
            folder,
            allowMultiSelect: false);
        return paths.Count > 0 ? paths[0] : null;
    }
}
