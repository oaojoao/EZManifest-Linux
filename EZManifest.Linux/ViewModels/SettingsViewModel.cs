using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EZManifest.Linux.Services;
using EZManifest.Models;
using EZManifest.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EZManifest.Linux.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsService _settingsService;
    private readonly ProtonService _protonService;
    private readonly AppMessageBoxService _messageBoxService;
    private readonly FileExplorerPickerService _filePicker;

    public ObservableCollection<string> ProtonVersions { get; } = [ProtonService.AutoVersion];

    [ObservableProperty]
    private string _downloadPath = string.Empty;

    [ObservableProperty]
    private string _protonVersion = ProtonService.AutoVersion;

    [ObservableProperty]
    private bool _useProton;

    [ObservableProperty]
    private string _protonGlobalPrefixPath = string.Empty;

    [ObservableProperty]
    private string _protonEnvironmentVariables = string.Empty;

    [ObservableProperty]
    private int _maxConcurrentChunks = AppSettings.DefaultMaxConcurrentChunks;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public SettingsViewModel(
        AppSettingsService settingsService,
        ProtonService protonService,
        AppMessageBoxService messageBoxService,
        FileExplorerPickerService filePicker)
    {
        _settingsService = settingsService;
        _protonService = protonService;
        _messageBoxService = messageBoxService;
        _filePicker = filePicker;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var settings = await _settingsService.LoadAsync();
        DownloadPath = settings.DownloadPath;
        UseProton = settings.UseProton;
        ProtonVersion = settings.ProtonVersion;
        ProtonGlobalPrefixPath = settings.ProtonGlobalPrefixPath;
        ProtonEnvironmentVariables = settings.ProtonEnvironmentVariables;
        MaxConcurrentChunks = settings.MaxConcurrentChunks;
        ProtonVersions.Clear();
        ProtonVersions.Add(ProtonService.AutoVersion);
        foreach (var version in _protonService.GetInstalledVersions())
            ProtonVersions.Add(version.Name);
    }

    [RelayCommand]
    private async Task BrowseDownloadPathAsync()
    {
        string? folder = await _filePicker.PickFolderAsync("Select install folder");
        if (!string.IsNullOrWhiteSpace(folder))
            DownloadPath = folder;
    }

    [RelayCommand]
    private async Task ApplyDownloadPathAsync()
    {
        try
        {
            await _settingsService.UpdateAsync(settings => settings.DownloadPath = DownloadPath.Trim());
            StatusText = $"Install path set to {DownloadPath}";
            await _messageBoxService.ShowAsync("Path applied",
                $"Download path set to:\n{DownloadPath}\n\nNew downloads will use this folder.");
        }
        catch (Exception ex)
        {
            await _messageBoxService.ShowAsync("Could not save settings", ex.Message);
        }
    }

    partial void OnUseProtonChanged(bool value)
    {
        _ = _settingsService.UpdateAsync(settings => settings.UseProton = value);
    }

    partial void OnProtonVersionChanged(string value)
    {
        _ = _settingsService.UpdateAsync(settings => settings.ProtonVersion = value);
    }

    partial void OnProtonGlobalPrefixPathChanged(string value)
    {
        _ = _settingsService.UpdateAsync(settings => settings.ProtonGlobalPrefixPath = value?.Trim() ?? string.Empty);
    }

    partial void OnProtonEnvironmentVariablesChanged(string value)
    {
        _ = _settingsService.UpdateAsync(settings => settings.ProtonEnvironmentVariables = value?.Trim() ?? string.Empty);
    }

    [RelayCommand]
    private async Task BrowsePrefixPathAsync()
    {
        string? folder = await _filePicker.PickFolderAsync("Select global WINE prefix folder");
        if (!string.IsNullOrWhiteSpace(folder))
            ProtonGlobalPrefixPath = folder;
    }

    partial void OnMaxConcurrentChunksChanged(int value)
    {
        _ = _settingsService.UpdateAsync(settings => settings.MaxConcurrentChunks = value);
    }
}
