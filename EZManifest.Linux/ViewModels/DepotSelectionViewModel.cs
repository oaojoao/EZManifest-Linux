using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EZManifest.Linux.Services;

namespace EZManifest.Linux.ViewModels;

public partial class DepotSelectionViewModel : ObservableObject
{
    public string Title { get; }
    public string Hint { get; }
    public string PrimaryText { get; }
    public string CloseText { get; }
    public bool ShowInstallOptions { get; }

    public ObservableCollection<DepotRow> Rows { get; }

    [ObservableProperty]
    private bool _removeSteamDrm = true;

    [ObservableProperty]
    private bool _addToSteam;

    public DepotSelectionViewModel(
        string title,
        string hint,
        IReadOnlyList<DepotRow> rows,
        string primaryText,
        string closeText,
        bool showInstallOptions)
    {
        Title = title;
        Hint = hint;
        Rows = new ObservableCollection<DepotRow>(rows);
        PrimaryText = primaryText;
        CloseText = closeText;
        ShowInstallOptions = showInstallOptions;
    }
}

public partial class ExeSelectionViewModel : ObservableObject
{
    public string Title { get; }
    public IReadOnlyList<string> Executables { get; }

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private bool _allowBrowse;

    [ObservableProperty]
    private bool _browseRequested;

    [RelayCommand]
    private void Browse() => BrowseRequested = true;

    public ExeSelectionViewModel(string title, IReadOnlyList<string> executables)
    {
        Title = title;
        Executables = executables;
    }
}

/// <summary>
/// Per-game Proton settings shown in an in-page popup: WINE prefix override and
/// extra environment variables. Values are copied from the game on open and only
/// written back when the user presses Save.
/// </summary>
public partial class ProtonSettingsViewModel : ObservableObject
{
    private readonly EZManifest.Models.GameEntry _game;
    private readonly FileExplorerPickerService? _filePicker;
    private readonly Func<Task> _persistAsync;

    public string GameName { get; }

    [ObservableProperty]
    private string _prefixPath;

    [ObservableProperty]
    private string _environmentVariables;

    public ProtonSettingsViewModel(
        EZManifest.Models.GameEntry game,
        FileExplorerPickerService? filePicker,
        Func<Task> persistAsync)
    {
        _game = game;
        _filePicker = filePicker;
        _persistAsync = persistAsync;
        GameName = game.Name;
        _prefixPath = game.ProtonPrefixPath;
        _environmentVariables = game.ProtonEnvironmentVariables;
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (_filePicker is null)
            return;
        string? folder = await _filePicker.PickFolderAsync($"Select WINE prefix for {GameName}");
        if (!string.IsNullOrWhiteSpace(folder))
            PrefixPath = folder;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        _game.ProtonPrefixPath = PrefixPath?.Trim() ?? string.Empty;
        _game.ProtonEnvironmentVariables = EnvironmentVariables?.Trim() ?? string.Empty;
        await _persistAsync();
    }
}
