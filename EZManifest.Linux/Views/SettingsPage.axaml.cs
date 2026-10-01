using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Markup.Xaml;
using EZManifest.Linux.ViewModels;

namespace EZManifest.Linux.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = App.Services.GetRequiredService<SettingsViewModel>();
    }
}
