using Avalonia;
using EZManifest.Linux;
using Microsoft.Extensions.DependencyInjection;

// The whole application targets Linux desktops only; this context removes
// CA1416 warnings at every File.GetUnixFileMode / SetUnixFileMode call site.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("linux")]

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
