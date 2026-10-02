using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EZManifest.Linux.Services;
using EZManifest.Models;
using EZManifest.Services;

namespace EZManifest.Linux.ViewModels;

public partial class PatchViewModel : ObservableObject
{
    private readonly PatchApplyService _patchApply;
    private readonly AppMessageBoxService _messageBoxService;
    private readonly FileExplorerPickerService _filePicker;
    private readonly GameLibraryService _gameLibrary;

    public ObservableCollection<GameEntry> InstalledGames { get; } = [];

    [ObservableProperty]
    private string _statusText = "Pick a downloaded patch archive (.zip/.7z/.rar), then choose the game to patch.";

    [ObservableProperty]
    private bool _isBusy;

    private string? _patchArchivePath;
    private string? _extractedPatchRoot;

    public PatchViewModel(
        PatchApplyService patchApply,
        AppMessageBoxService messageBoxService,
        FileExplorerPickerService filePicker,
        GameLibraryService gameLibrary)
    {
        _patchApply = patchApply;
        _messageBoxService = messageBoxService;
        _filePicker = filePicker;
        _gameLibrary = gameLibrary;
        _ = LoadInstalledGamesAsync();
    }

    public async Task LoadInstalledGamesAsync()
    {
        try
        {
            var games = await _gameLibrary.LoadAsync();
            InstalledGames.Clear();
            foreach (var game in games.Where(g => g.IsInstalled && !string.IsNullOrWhiteSpace(g.InstallPath)))
                InstalledGames.Add(game);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "[Patch] Failed to load installed games");
        }
    }

    [RelayCommand]
    private async Task BrowsePatchArchiveAsync()
    {
        try
        {
        var paths = await _filePicker.PickFilesAsync([".zip", ".7z", ".rar"], "Select patch archive");
        if (paths.Count == 0)
            return;

        _patchArchivePath = paths[0];
        await ExtractPatchAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "[Patch] Browse failed");
        }
    }

    private async Task ExtractPatchAsync()
    {
        if (string.IsNullOrWhiteSpace(_patchArchivePath))
            return;
        IsBusy = true;
        StatusText = "Extracting patch archive...";
        try
        {
            _extractedPatchRoot = await _patchApply.ExtractArchiveAsync(
                _patchArchivePath,
                null,
                CancellationToken.None);
            StatusText = "Patch extracted. Select the game to patch, then Apply.";
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "[Patch] Extract failed");
            StatusText = $"Extract failed: {AppLog.GetRootMessage(ex)}";
            await _messageBoxService.ShowAsync("Patch extract failed", AppLog.GetRootMessage(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyPatchAsync()
    {
        if (string.IsNullOrWhiteSpace(_extractedPatchRoot))
        {
            await _messageBoxService.ShowAsync("No patch loaded", "Pick a patch archive first.");
            return;
        }

        var games = await _gameLibrary.LoadAsync();
        var installed = games
            .Where(g => g.IsInstalled && !string.IsNullOrWhiteSpace(g.InstallPath))
            .ToList();
        if (installed.Count == 0)
        {
            await _messageBoxService.ShowAsync(
                "No installed games",
                "Install a game through the Downloads page first, or verify the library.");
            return;
        }

        var vm = new PatchTargetSelectionViewModel(
            "Choose the game to patch",
            installed.Select(g => $"{g.Name} ({g.InstallPath})").ToList());
        var page = new Views.PatchTargetSelectionDialog { DataContext = vm };
        var result = await _messageBoxService.ShowDialogAsync(page, "Apply patch", "Cancel");
        if (result != ContentDialogResult.Primary || vm.SelectedIndex < 0 || vm.SelectedIndex >= installed.Count)
            return;

        GameEntry target = installed[vm.SelectedIndex];
        await ApplyPatchToGameAsync(target);
    }

    private async Task ApplyPatchToGameAsync(GameEntry game)
    {
        IsBusy = true;
        StatusText = $"Applying patch to {game.Name}...";
        var progress = new Progress<PatchApplyProgress>(p =>
        {
            StatusText = $"{p.Phase}: {p.Done}/{p.Total}" +
                (string.IsNullOrWhiteSpace(p.Current) ? "" : $" - {p.Current}");
        });
        try
        {
            string? sourceFolder = PatchApplyService.FindPatchSourceFolder(_extractedPatchRoot!);
            if (string.IsNullOrWhiteSpace(sourceFolder))
            {
                await _messageBoxService.ShowAsync(
                    "Patch not found",
                    "Could not find a patch file structure inside the archive.");
                return;
            }

            int copied = await _patchApply.CopyFilesOverAsync(sourceFolder, game.InstallPath!, progress, CancellationToken.None);
            StatusText = $"Patch applied to {game.Name}: {copied} file(s) copied.";
            await _messageBoxService.ShowAsync("Patch applied", $"Copied {copied} file(s) to {game.InstallPath}");
            await LoadInstalledGamesAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"[Patch] Failed to apply patch to '{game.Name}'");
            StatusText = $"Patch failed: {AppLog.GetRootMessage(ex)}";
            await _messageBoxService.ShowAsync("Patch failed", AppLog.GetRootMessage(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public partial class PatchTargetSelectionViewModel : ObservableObject
{
    public string Title { get; }
    public IReadOnlyList<string> Games { get; }

    [ObservableProperty]
    private int _selectedIndex = -1;

    public PatchTargetSelectionViewModel(string title, IReadOnlyList<string> games)
    {
        Title = title;
        Games = games;
    }
}
