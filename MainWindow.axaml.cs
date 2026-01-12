using Avalonia.Controls;
using DaevaMini.Views;
using DaevaMini.ViewModels;

namespace DaevaMini;

public partial class MainWindow : Window
{
    private ContentControl? _pageContainer;
    private readonly ModesViewModel _modesViewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        _pageContainer = this.FindControl<ContentControl>("PageContainer");
        ShowSplash();
    }

    public void ShowSplash()
    {
        if (_pageContainer != null)
        {
            _pageContainer.Content = new SplashPage(_modesViewModel);
        }
    }

    public void ShowCocktailsMenu()
    {
        if (_pageContainer != null)
        {
            _pageContainer.Content = new CocktailsMenu(_modesViewModel);
        }
    }
    
    public void ShowSettingsPage()
    {
        if (_pageContainer != null)
        {
            _pageContainer.Content = new SettingsPage(_modesViewModel);
        }
    }
    
    public void ShowContainerSetupPage()
    {
        if (_pageContainer != null)
        {
            _pageContainer.Content = new ContainerSetupPage(_modesViewModel);
        }
    }
}
