using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using EZManifest.Linux.ViewModels;
using EZManifest.Models;
using Microsoft.Extensions.DependencyInjection;
using WebViewControl;

namespace EZManifest.Linux.Views;

public partial class LibraryPage : UserControl
{
    public LibraryPage()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = App.Services.GetRequiredService<LibraryViewModel>();
    }

    private void OnCardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Border { DataContext: Models.GameEntry game } border
            && DataContext is LibraryViewModel vm)
        {
            vm.SelectGameCommand.Execute(game);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Loads the next chunk of games while the grid is still being scrolled, so
    /// the library grows seamlessly instead of realizing every card at once.
    /// </summary>
    private void OnLibraryScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll
            || DataContext is not LibraryViewModel vm
            || vm.RemainingGames <= 0)
            return;
        if (scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 600)
            vm.LoadMoreGamesCommand.Execute(null);
    }

    /// <summary>
    /// Shows media inside the app: images in a full-page overlay, videos in a
    /// modal window hosting the embedded CEF WebView (Chromium's built-in player).
    /// </summary>
    private async void OnOpenMedia(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not GameMediaItem item
            || DataContext is not LibraryViewModel vm)
            return;

        if (item.IsVideo)
        {
            string? url = item.VideoUris.Count > 0 ? item.VideoUris[0].ToString() : item.VideoUrl;
            if (!string.IsNullOrWhiteSpace(url))
                await OpenVideoWindowAsync(url);
        }
        else if (!string.IsNullOrWhiteSpace(item.ImageUrl))
        {
            vm.ViewedMediaImageUrl = item.ImageUrl;
        }
    }

    private async Task OpenVideoWindowAsync(string url)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return;

        Services.WebViewSetup.Configure();
        var web = new WebView();
        web.UnhandledAsyncException += error =>
            EZManifest.Services.AppLog.Write(error.Exception, "[Library] Media WebView error");

        var dialog = new Window
        {
            Title = "Game media",
            Width = 980,
            Height = 600,
            CanResize = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.Black,
            Content = new Border { Padding = new Avalonia.Thickness(0), Child = web }
        };
        web.LoadUrl(url);
        await dialog.ShowDialog(owner);
    }

    /// <summary>Closes the image overlay when the backdrop itself is clicked.</summary>
    private void OnMediaOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender)
            && DataContext is LibraryViewModel vm)
            vm.CloseMediaViewerCommand.Execute(null);
    }
}
