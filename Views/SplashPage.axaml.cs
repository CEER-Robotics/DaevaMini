using Avalonia.Controls;
using Avalonia.Interactivity;
using DaevaMini.ViewModels;

namespace DaevaMini.Views;

public partial class SplashPage : UserControl
{

    public SplashPage()
    {
        InitializeComponent();
        DataContext = new ModesViewModel();
    }

    public void OnTouchToStart(object? sender, RoutedEventArgs e)
    {
        // Normal navigation
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowCocktailsMenu();
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSettingsPage();
    }
}