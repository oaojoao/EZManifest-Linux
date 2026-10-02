using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace EZManifest.Linux;

public partial class MainWindow : Window
{
    private readonly IServiceProvider? _services;

    /// <summary>Set at startup so view models can request page navigation.</summary>
    public static Action<string>? NavigateRequested;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public MainWindow(IServiceProvider services) : this()
    {
        _services = services;
        NavigateRequested = ShowPage;
        ShowPage("Library");
    }

    private void OnNavigateLibrary(object sender, RoutedEventArgs e) => ShowPage("Library");
    private void OnNavigateDepotBox(object sender, RoutedEventArgs e) => ShowPage("DepotBox");
    private void OnNavigateDownloads(object sender, RoutedEventArgs e) => ShowPage("Downloads");
    private void OnNavigatePatch(object sender, RoutedEventArgs e) => ShowPage("Patch");
    private void OnNavigateSettings(object sender, RoutedEventArgs e) => ShowPage("Settings");

    private void ShowPage(string page)
    {
        if (_services is null)
            return;
        object content = page switch
        {
            "DepotBox" => _services.GetRequiredService<Views.DepotBoxPage>(),
            "Downloads" => _services.GetRequiredService<Views.DownloadsPage>(),
            "Patch" => _services.GetRequiredService<Views.PatchPage>(),
            "Settings" => _services.GetRequiredService<Views.SettingsPage>(),
            _ => _services.GetRequiredService<Views.LibraryPage>()
        };
        var host = this.Find<ContentControl>("PageHost");
        if (host is not null)
            host.Content = content;
    }
}
