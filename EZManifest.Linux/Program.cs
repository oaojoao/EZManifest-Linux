using Avalonia;
using EZManifest.Linux;
using Microsoft.Extensions.DependencyInjection;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        App.Services = provider;

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
