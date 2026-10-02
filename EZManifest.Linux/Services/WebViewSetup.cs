using WebViewControl;

namespace EZManifest.Linux.Services;

/// <summary>
/// WebViewControl's global settings are static and can only be configured
/// before the first WebView is created ("Cannot set OsrEnabled after WebView
/// engine has been loaded"). Every page with an embedded browser must call
/// <see cref="Configure"/> before creating its WebView; only the first call
/// applies the settings.
/// </summary>
public static class WebViewSetup
{
    private static bool _configured;

    public static void Configure()
    {
        if (_configured)
            return;
        _configured = true;

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
    }
}
