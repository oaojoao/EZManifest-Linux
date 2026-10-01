namespace EZManifest.Linux.Services;

/// <summary>
/// Tracks the single Avalonia main window so services can show dialogs and pickers.
/// </summary>
public sealed class WindowProvider
{
    private Avalonia.Controls.Window? _window;

    public Avalonia.Controls.Window? Window => _window;

    public void SetWindow(Avalonia.Controls.Window window) => _window = window;

    public void Clear() => _window = null;
}
