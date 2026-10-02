using Avalonia;
using Avalonia.Markup.Xaml;
using EZManifest.Linux.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EZManifest.Linux;

public class App : Avalonia.Application
{
    public static IServiceProvider Services { get; internal set; } = null!;

    public App()
    {
        AvaloniaXamlLoader.Load(this);
    }


    public override void OnFrameworkInitializationCompleted()
    {
        EZManifest.Services.AppLog.Write(
            $"EZManifest Linux started (version {typeof(App).Assembly.GetName().Version})");
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = Services.GetRequiredService<MainWindow>();
            var windowProvider = Services.GetRequiredService<WindowProvider>();
            windowProvider.SetWindow(window);
            desktop.MainWindow = window;
            window.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<System.Net.Http.HttpClient>();
        services.AddSingleton<WindowProvider>();
        services.AddSingleton<AppMessageBoxService>();
        services.AddSingleton<FileExplorerPickerService>();
        services.AddSingleton<EZManifest.Services.AppSettingsService>();
        services.AddSingleton<EZManifest.Services.DebugLogService>();
        services.AddSingleton<EZManifest.Services.GameLibraryService>();
        services.AddSingleton<EZManifest.Services.LuaManifestParser>();
        services.AddSingleton<PostDownloadService>();
        services.AddSingleton<EZManifest.Services.ManifestArchiveService>();
        services.AddSingleton<EZManifest.Services.SteamMetadataService>();
        services.AddSingleton<EZManifest.Services.SteamDepotMetadataService>();
        services.AddSingleton<GameInstallPathService>();
        services.AddSingleton<EZManifest.Services.GameInstallSizeService>();
        services.AddSingleton<GameUninstallService>();
        services.AddSingleton<ShortcutService>();
        services.AddSingleton<SteamNonSteamShortcutService>();
        services.AddSingleton<EZManifest.Services.ProtonService>();
        services.AddSingleton<GameLauncher>();
        services.AddSingleton<AppNotificationService>();
        services.AddSingleton<PatchApplyService>();
        services.AddSingleton<ViewModels.LibraryViewModel>();
        services.AddSingleton<ViewModels.DownloadsViewModel>();
        services.AddSingleton<ViewModels.DepotBoxViewModel>();
        services.AddSingleton<ViewModels.PatchViewModel>();
        services.AddSingleton<ViewModels.SettingsViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<Views.LibraryPage>();
        services.AddSingleton<Views.DownloadsPage>();
        services.AddSingleton<Views.DepotBoxPage>();
        services.AddSingleton<Views.PatchPage>();
        services.AddSingleton<Views.SettingsPage>();
    }
}
