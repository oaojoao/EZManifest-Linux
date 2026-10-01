using System.Text;
using EZManifest.Services;

namespace EZManifest.Linux.Services;

/// <summary>
/// Writes a Linux notification via notify-send (freedesktop standard).
/// </summary>
public sealed class AppNotificationService
{
    public void ShowInstallCompleted(string gameName)
    {
        TrySend("EZManifest", $"{gameName} finished installing.", "dialog-information");
    }

    public void ShowInstallFailed(string gameName, string reason)
    {
        TrySend("EZManifest", $"{gameName} failed to install: {reason}", "dialog-error");
    }

    private static void TrySend(string summary, string body, string icon)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "notify-send",
                Arguments = $"-a EZManifest -i {icon} \"{Escape(summary)}\" \"{Escape(body)}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write($"[Notify] notify-send failed: {ex.Message}");
        }
    }

    private static string Escape(string value) =>
        value.Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
}
