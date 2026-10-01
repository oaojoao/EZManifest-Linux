using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace EZManifest.Linux;

public partial class MainWindow : Window
{
    private readonly IServiceProvider? _services;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public MainWindow(IServiceProvider services) : this()
    {
        _services = services;
        ShowPage("Library");
    }

    private void OnNavigateLibrary(object sender, RoutedEventArgs e) => ShowPage("Library");
    private void OnNavigateDownloads(object sender, RoutedEventArgs e) => ShowPage("Downloads");
    private void OnNavigateSettings(object sender, RoutedEventArgs e) => ShowPage("Settings");

    private void ShowPage(string page)
    {
        if (_services is null)
            return;
        object content = page switch
        {
            "Downloads" => _services.GetRequiredService<Views.DownloadsPage>(),
            "Settings" => _services.GetRequiredService<Views.SettingsPage>(),
            _ => _services.GetRequiredService<Views.LibraryPage>()
        };
        var host = this.Find<ContentControl>("PageHost");
        if (host is not null)
            host.Content = content;
    }
}
