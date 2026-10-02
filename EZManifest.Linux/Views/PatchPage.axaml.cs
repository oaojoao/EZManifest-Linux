using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using EZManifest.Linux.ViewModels;
using EZManifest.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EZManifest.Linux.Views;

public partial class PatchPage : UserControl
{
    public PatchPage()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = App.Services.GetRequiredService<PatchViewModel>();
    }

    private void OnOpenGamecopyworld(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = "https://www.gamecopyworld.com/",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "[Patch] Could not open browser");
        }
    }
}
