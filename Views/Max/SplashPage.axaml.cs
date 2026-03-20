using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DaevaMini.Views.Max;

public partial class SplashPage : UserControl
{
    public SplashPage()
    {
        InitializeComponent();
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