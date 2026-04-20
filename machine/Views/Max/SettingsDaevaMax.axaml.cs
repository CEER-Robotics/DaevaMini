using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DaevaMini.Views.Max;

public partial class SettingsDaevaMax : UserControl
{
    public SettingsDaevaMax()
    {
        InitializeComponent();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSplash();
    }

    private void OnFillingClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowContainerFillPage();
    }

    private void OnCleaningClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowContainerCleanPage();
    }
}