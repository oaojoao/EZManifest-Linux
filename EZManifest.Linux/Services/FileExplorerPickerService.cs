using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace EZManifest.Linux.Services;

/// <summary>
/// Linux file/folder picker built on Avalonia's IStorageProvider (GTK portal on Linux).
/// </summary>
public sealed class FileExplorerPickerService
{
    private readonly WindowProvider _windowProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileExplorerPickerService(WindowProvider windowProvider) =>
        _windowProvider = windowProvider;

    public async Task<string?> PickFolderAsync(
        string title = "Select folder",
        string? initialPath = null,
        bool startAtThisPc = false)
    {
        IReadOnlyList<string> paths = await PickAsync(
            title,
            foldersOnly: true,
            allowMultiSelect: false,
            extensions: null,
            initialPath);
        return paths.Count > 0 ? paths[0] : null;
    }

    public async Task<IReadOnlyList<string>> PickFoldersAsync(
        string title = "Select folders",
        string? initialPath = null)
    {
        return await PickAsync(title, foldersOnly: true, allowMultiSelect: true, extensions: null, initialPath);
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync(
        IReadOnlyList<string> extensions,
        string title = "Open",
        string? initialPath = null,
        bool allowMultiSelect = true)
    {
        return await PickAsync(title, foldersOnly: false, allowMultiSelect: allowMultiSelect, extensions, initialPath);
    }

    private async Task<IReadOnlyList<string>> PickAsync(
        string title,
        bool foldersOnly,
        bool allowMultiSelect,
        IReadOnlyList<string>? extensions,
        string? initialPath)
    {
        await _gate.WaitAsync();
        try
        {
            var window = _windowProvider.Window
                ?? throw new InvalidOperationException("Main window is not available.");

            IStorageProvider storage = window.StorageProvider;

            IStorageFolder? startLocation = null;
            if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
            {
                try
                {
                    startLocation = await storage.TryGetFolderAsync(initialPath);
                }
                catch
                {
                    startLocation = null;
                }
            }

            if (foldersOnly)
            {
                var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = allowMultiSelect,
                    SuggestedStartLocation = startLocation
                });
                return result.Select(folder => folder.TryGetLocalPath()).OfType<string>().ToList();
            }

            var fileTypes = extensions?
                .Where(ext => !string.IsNullOrWhiteSpace(ext))
                .Select(ext =>
                {
                    string pattern = ext.StartsWith('.') ? ext : "." + ext;
                    return new FilePickerFileType(ext.ToUpperInvariant())
                    {
                        Patterns = [$"*{pattern}"]
                    };
                })
                .ToList();

            var fileResult = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = allowMultiSelect,
                FileTypeFilter = fileTypes,
                SuggestedStartLocation = startLocation
            });
            return fileResult.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        }
        finally
        {
            _gate.Release();
        }
    }
}
