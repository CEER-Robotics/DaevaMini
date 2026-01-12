using Avalonia.Controls;
using Avalonia.Interactivity;
using DaevaMini.ViewModels;

namespace DaevaMini.Views;

public partial class ContainerSetupPage : UserControl
{
    private readonly ModesViewModel _modesViewModel;

    public ContainerSetupPage(ModesViewModel modesViewModel)
    {
        InitializeComponent();
        _modesViewModel = modesViewModel;
        DataContext = _modesViewModel;
    }

    private void OnBackArrow(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSettingsPage();
    }

    private void OnDone(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSplash();
    }
}

