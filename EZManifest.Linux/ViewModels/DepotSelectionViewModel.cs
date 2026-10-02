using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
