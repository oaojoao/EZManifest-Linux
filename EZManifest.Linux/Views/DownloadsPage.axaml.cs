using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EZManifest.Linux.ViewModels;

namespace EZManifest.Linux.Views;

public partial class DownloadsPage : UserControl
{
    public DownloadsPage()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = App.Services.GetRequiredService<DownloadsViewModel>();
    }
}
