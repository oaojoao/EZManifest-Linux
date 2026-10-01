using System.Threading;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace EZManifest.Linux.Services;

public enum ContentDialogResult
{
    None = 0,
    Primary,
    Secondary
}

/// <summary>
/// ContentDialog-compatible message box: a modal overlay over the main window.
/// </summary>
public sealed class AppMessageBoxService
{
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private readonly WindowProvider _windowProvider;

    public AppMessageBoxService(WindowProvider windowProvider) =>
        _windowProvider = windowProvider;

    public async Task<ContentDialogResult> ShowAsync(
        string title,
        string content,
        string? primaryButtonText = null,
        string closeButtonText = "OK",
        string? secondaryButtonText = null)
    {
        await _dialogGate.WaitAsync();
        try
        {
            var window = _windowProvider.Window
                ?? throw new InvalidOperationException("Main window is not available yet.");

            return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var completion = new TaskCompletionSource<ContentDialogResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                var panel = new StackPanel { Spacing = 16, MinWidth = 320, MaxWidth = 480 };
                var text = new TextBlock
                {
                    Text = content,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460
                };
                panel.Children.Add(text);

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                void AddButton(string label, ContentDialogResult result, bool isPrimary)
                {
                    var button = new Button { Content = label, MinWidth = 110 };
                    button.Click += (_, _) => completion.TrySetResult(result);
                    buttons.Children.Add(button);
                }

                if (!string.IsNullOrWhiteSpace(secondaryButtonText))
                    AddButton(secondaryButtonText, ContentDialogResult.Secondary, false);
                if (!string.IsNullOrWhiteSpace(primaryButtonText))
                    AddButton(primaryButtonText, ContentDialogResult.Primary, true);
                AddButton(closeButtonText, ContentDialogResult.None, false);
                panel.Children.Add(buttons);

                var dialog = new Window
                {
                    Title = title,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    CanResize = false,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new Border
                    {
                        Padding = new Thickness(24),
                        BorderThickness = new Thickness(1),
                        BorderBrush = Brushes.Gray,
                        CornerRadius = new CornerRadius(8),
                        Child = panel
                    }
                };

                dialog.Closed += (_, _) => completion.TrySetResult(ContentDialogResult.None);

                dialog.Show(window);
                var result = await completion.Task;
                if (dialog.IsVisible)
                    dialog.Close();
                return result;
            });
        }
        finally
        {
            _dialogGate.Release();
        }
    }
}
