using Avalonia.Controls;
using Avalonia.Interactivity;
using DaevaMini.ViewModels;

namespace DaevaMini;

public partial class SettingsDaevaMax : UserControl
{
    public SettingsDaevaMax()
    {
        InitializeComponent();
    }

    public SettingsDaevaMax(ModesViewModel modesViewModel) : this()
    {
        DataContext = modesViewModel;
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