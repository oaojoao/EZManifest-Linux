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

        // Global CEF settings (OSR/software rendering, cache) are applied once by
        // the first embedded browser; re-setting them crashes once loaded.
        Services.WebViewSetup.Configure();

        _webView = new WebView();
        // The internal chromium control mirrors this property; without it the
        // off-screen browser never receives focus and text fields ignore clicks.
        _webView.Focusable = true;
        // Avalonia does not focus controls on click (unlike WPF): request focus
        // explicitly so the browser can hand it to the HTML text fields.
        _webView.PointerPressed += (_, _) => _webView.Focus();
        _webView.Navigated += (url, _) => vm.OnNavigated(url);
        _webView.UnhandledAsyncException += e => EZManifest.Services.AppLog.Write(e.Exception, "[DepotBox] WebView error");
        InstallDownloadHandler(vm);
        Services.SameTabPopupInstaller.Install(_webView);

        var host = this.Find<Panel>("BrowserHost");
        host?.Children.Add(_webView);

        vm.NavigateRequested = url => _webView.LoadUrl(url);
        _webView.LoadUrl(vm.Address);
    }

    private WebView? WebViewControl => _webView;

    /// <summary>
    /// WebViewControl's built-in download handler opens a native GTK file dialog
    /// (showDialog: true) which crashes inside the AppImage (no GTK bundled).
    /// Replace it with an automatic, dialog-less download handler.
    /// </summary>
    private void InstallDownloadHandler(DepotBoxViewModel vm)
    {
        try
        {
            var browserProperty = typeof(WebView).GetProperty(
                "UnderlyingBrowser",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var chromium = browserProperty?.GetValue(_webView);
            var handlerProperty = chromium?.GetType().GetProperty("DownloadHandler");
            if (chromium is null || handlerProperty is null)
            {
                EZManifest.Services.AppLog.Write("[DepotBox] Download handler replacement unavailable, falling back to built-in");
                _webView.DownloadCompleted += path => vm.OnDownloadCompleted(0, path);
                _webView.DownloadCancelled += path => vm.OnDownloadCancelled(0, path);
                _webView.DownloadProgressChanged += (path, received, total) => vm.OnDownloadProgress(0, path, received, total);
                return;
            }
            handlerProperty.SetValue(chromium, new Services.DepotBoxDownloadHandler(
                vm.OnDownloadStarted,
                vm.OnDownloadProgress,
                vm.OnDownloadCompleted,
                vm.OnDownloadCancelled));
        }
        catch (Exception ex)
        {
            EZManifest.Services.AppLog.Write(ex, "[DepotBox] Could not install custom download handler");
        }
    }

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
