using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using EZManifest.Linux.ViewModels;
using EZManifest.Linux.Services;
using EZManifest.Services;
using Microsoft.Extensions.DependencyInjection;
using WebViewControl;

namespace EZManifest.Linux.Views;

public partial class PatchPage : UserControl
{
    private WebView? _webView;

    public PatchPage()
    {
        AvaloniaXamlLoader.Load(this);
        var vm = App.Services.GetRequiredService<PatchViewModel>();
        DataContext = vm;

        // Same off-screen/software CEF configuration as DepotBox (the settings are
        // global; repeated in case this page is opened first).
        WebView.Settings.OsrEnabled = true;
        WebView.Settings.AddCommandLineSwitch("no-sandbox", null);
        WebView.Settings.AddCommandLineSwitch("disable-setuid-sandbox", null);
        WebView.Settings.AddCommandLineSwitch("disable-gpu", null);
        WebView.Settings.AddCommandLineSwitch("disable-gpu-compositing", null);
        WebView.Settings.CachePath = System.IO.Path.Combine(AppPaths.DataDirectory, "WebViewCache");
        WebView.Settings.PersistCache = true;

        _webView = new WebView
        {
            Focusable = true
        };
        _webView.PointerPressed += (_, _) => _webView.Focus();
        _webView.UnhandledAsyncException += e => AppLog.Write(e.Exception, "[Patch] WebView error");
        InstallDownloadHandler(vm);

        var host = this.Find<Panel>("GcwBrowserHost");
        host?.Children.Add(_webView);

        vm.NavigateRequested = url => _webView.LoadUrl(url);
        _webView.LoadUrl(PatchViewModel.GameCopyWorldHomeUrl);
    }

    /// <summary>
    /// WebViewControl's built-in download handler opens a native GTK file dialog
    /// that crashes inside the AppImage. Reuse the dialog-less DepotBox handler so
    /// GameCopyWorld patch archives download straight into ~/Downloads.
    /// </summary>
    private void InstallDownloadHandler(PatchViewModel vm)
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
                AppLog.Write("[Patch] Download handler replacement unavailable, falling back to built-in");
                _webView.DownloadCompleted += path => vm.OnDownloadCompleted(path);
                _webView.DownloadCancelled += path => vm.OnDownloadCancelled(path);
                _webView.DownloadProgressChanged += (path, received, total) => vm.OnDownloadProgress(path, received, total);
                return;
            }
            handlerProperty.SetValue(chromium, new DepotBoxDownloadHandler(
                vm.OnDownloadStarted,
                vm.OnDownloadProgress,
                vm.OnDownloadCompleted,
                vm.OnDownloadCancelled));
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "[Patch] Could not install custom download handler");
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

    private void OnGoHome(object sender, RoutedEventArgs e)
    {
        _webView?.LoadUrl(PatchViewModel.GameCopyWorldHomeUrl);
    }

    private void OnOpenExternal(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = _webView?.Address ?? PatchViewModel.GameCopyWorldHomeUrl,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "[Patch] Could not open external browser");
        }
    }
}
