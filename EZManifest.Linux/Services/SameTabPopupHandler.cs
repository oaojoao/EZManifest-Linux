using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;

namespace EZManifest.Linux.Services;

/// <summary>
/// Cancels native CEF popup windows (target=_blank / window.open, used heavily
/// by GameCopyWorld ads) and loads the target URL in the existing WebView
/// instead, so navigation always stays inside the app.
/// </summary>
public sealed class SameTabPopupHandler : LifeSpanHandler
{
    private readonly Action<string> _openInSameView;

    public SameTabPopupHandler(Action<string> openInSameView)
    {
        _openInSameView = openInSameView;
    }

    protected override bool OnBeforePopup(
        CefBrowser browser,
        CefFrame frame,
        string targetUrl,
        string targetFrameName,
        CefWindowOpenDisposition targetDisposition,
        bool userGesture,
        CefPopupFeatures popupFeatures,
        CefWindowInfo windowInfo,
        ref CefClient client,
        CefBrowserSettings settings,
        ref CefDictionaryValue extraInfo,
        ref bool noJavascriptAccess)
    {
        if (!string.IsNullOrWhiteSpace(targetUrl))
        {
            // OnBeforePopup runs on a CEF thread; marshal to the UI thread.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _openInSameView(targetUrl));
        }
        return true; // never let CEF open a native popup window
    }
}

/// <summary>
/// Installs <see cref="SameTabPopupHandler"/> on a WebViewControl WebView via the
/// same reflection path used for the download handler (the underlying chromium
/// control is not exposed publicly).
/// </summary>
public static class SameTabPopupInstaller
{
    public static void Install(WebViewControl.WebView webView)
    {
        try
        {
            var browserProperty = typeof(WebViewControl.WebView).GetProperty(
                "UnderlyingBrowser",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var chromium = browserProperty?.GetValue(webView);
            var handlerProperty = chromium?.GetType().GetProperty("LifeSpanHandler");
            if (chromium is null || handlerProperty is null)
            {
                EZManifest.Services.AppLog.Write("[WebView] Popup handler replacement unavailable; links may open native windows");
                return;
            }
            handlerProperty.SetValue(chromium, new SameTabPopupHandler(url => webView.LoadUrl(url)));
        }
        catch (Exception ex)
        {
            EZManifest.Services.AppLog.Write(ex, "[WebView] Could not install the same-tab popup handler");
        }
    }
}
