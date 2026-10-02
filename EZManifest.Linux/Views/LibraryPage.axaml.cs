using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Markup.Xaml;
using EZManifest.Linux.ViewModels;

namespace EZManifest.Linux.Views;

public partial class LibraryPage : UserControl
{
    public LibraryPage()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = App.Services.GetRequiredService<LibraryViewModel>();
    }

    private void OnCardPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (sender is Border { DataContext: Models.GameEntry game } border
            && DataContext is LibraryViewModel vm)
        {
            _ = vm.SelectGameCommand.ExecuteAsync(game);
            e.Handled = true;
        }
    }
}
