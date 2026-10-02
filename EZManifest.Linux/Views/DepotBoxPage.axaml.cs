using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using EZManifest.Linux.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using WebViewControl;

namespace EZManifest.Linux.Views;

public partial class DepotBoxPage : UserControl
{
    private WebView? _webView;

    public DepotBoxPage()
    {
        AvaloniaXamlLoader.Load(this);
        var vm = App.Services.GetRequiredService<DepotBoxViewModel>();
        DataContext = vm;

        // Off-screen rendering: no native child X11 window and software
        // rendering, which is the stable mode for CEF inside an AppImage.
        WebView.Settings.OsrEnabled = true;
        WebView.Settings.AddCommandLineSwitch("no-sandbox", null);
        WebView.Settings.AddCommandLineSwitch("disable-setuid-sandbox", null);
        WebView.Settings.AddCommandLineSwitch("disable-gpu", null);
        WebView.Settings.AddCommandLineSwitch("disable-gpu-compositing", null);
        WebView.Settings.CachePath = System.IO.Path.Combine(
            EZManifest.Services.AppPaths.DataDirectory, "WebViewCache");
        WebView.Settings.PersistCache = true;

        _webView = new WebView();
        _webView.Navigated += (url, _) => vm.OnNavigated(url);
        _webView.DownloadCompleted += path => vm.OnDownloadCompleted(path);
        _webView.DownloadCancelled += path => vm.OnDownloadCancelled(path);
        _webView.DownloadProgressChanged += (path, received, total) => vm.OnDownloadProgress(path, received, total);
        _webView.UnhandledAsyncException += e => EZManifest.Services.AppLog.Write(e.Exception, "[DepotBox] WebView error");

        var host = this.Find<Panel>("BrowserHost");
        host?.Children.Add(_webView);

        vm.NavigateRequested = url => _webView.LoadUrl(url);
        _webView.LoadUrl(vm.Address);
    }

    private WebView? WebViewControl => _webView;

    private void OnGoBack(object sender, RoutedEventArgs e)
    {
        if (_webView?.CanGoBack == true)
            _webView.GoBack();
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        _webView?.Reload();
    }

    private void OnOpenExternal(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = _webView?.Address ?? (DataContext as DepotBoxViewModel)?.Address ?? "https://depotbox.org/",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            EZManifest.Services.AppLog.Write(ex, "[DepotBox] Could not open external browser");
        }
    }
}
