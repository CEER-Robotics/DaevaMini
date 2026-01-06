using Avalonia.Controls;
using DaevaMini.Views;

namespace DaevaMini;

public partial class MainWindow : Window
{
    private ContentControl? _pageContainer;

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
            _pageContainer.Content = new SplashPage();
        }
    }

    public void ShowCocktailsMenu()
    {
        if (_pageContainer != null)
        {
            _pageContainer.Content = new CocktailsMenu();
        }
    }
    
    public void ShowSettingsPage()
    {
        if (_pageContainer != null)
        {
            _pageContainer.Content = new SettingsPage();
        }
    }
}
