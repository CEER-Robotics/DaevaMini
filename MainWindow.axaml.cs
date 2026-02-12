using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DaevaMini.Config;
using DaevaMini.Controls;
using DaevaMini.Models;
using DaevaMini.Services;
using DaevaMini.Views;
using DaevaMini.ViewModels;

namespace DaevaMini;

public partial class MainWindow : Window
{
    private ContentControl? _pageContainer;
    private readonly ModesViewModel _modesViewModel = new();
    private readonly CocktailsMenuViewModel _cocktailsMenuViewModel;
    private UserControl? _cocktailMenuView;

    public MainWindow()
    {
        InitializeComponent();
        _pageContainer = this.FindControl<ContentControl>("PageContainer");
        _cocktailsMenuViewModel = new CocktailsMenuViewModel(_modesViewModel);
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
        if (_pageContainer == null) return;

        var repository = new InMemoryCocktailRepository();
        var menuViewModel = new CocktailMenuViewModel(repository, _modesViewModel)
        {
            SelectCocktailCommand = new RelayCommand(OnCocktailSelected)
        };

        var menu = new CocktailMenu
        {
            DataContext = menuViewModel
        };
        menu.BackClicked += OnCocktailMenuBackClicked;
        _cocktailMenuView = menu;
        _pageContainer.Content = menu;
    }

    private void OnCocktailMenuBackClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is CocktailMenu menu)
            menu.BackClicked -= OnCocktailMenuBackClicked;
        ShowSplash();
    }

    private void OnCocktailSelected(object? parameter)
    {
        if (parameter is not Cocktail cocktail) return;
        var card = new CocktailCard
        {
            DataContext = cocktail
        };
        card.CloseClicked += OnCardCloseClicked;
        card.DaleClicked += OnCardDaleClicked;
        if (_pageContainer != null)
            _pageContainer.Content = card;
    }

    private void OnCardCloseClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is CocktailCard card)
        {
            card.CloseClicked -= OnCardCloseClicked;
            card.DaleClicked -= OnCardDaleClicked;
        }
        if (_cocktailMenuView != null && _pageContainer != null)
            _pageContainer.Content = _cocktailMenuView;
    }

    private void OnCardDaleClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not CocktailCard card || card.DataContext is not Cocktail cocktail)
            return;
        if (_cocktailsMenuViewModel.IsDispensing) return;

        // Use ingredients from the cocktail (from appsettings via repository) when present
        CocktailVm? cocktailVm = null;
        if (cocktail.Ingredients.Count > 0)
        {
            var ingredients = cocktail.Ingredients
                .Select(i => new Ingredient(i.Name, i.Milliliters))
                .ToArray();
            cocktailVm = new CocktailVm(cocktail.Title, ingredients);
        }
        else
        {
            cocktailVm = _cocktailsMenuViewModel.Cocktails
                .FirstOrDefault(c => c.Name.Equals(cocktail.Title, StringComparison.OrdinalIgnoreCase));
        }

        if (cocktailVm == null)
        {
            Console.WriteLine($"[MainWindow] No matching cocktail config for: {cocktail.Title}");
            return;
        }

        _cocktailsMenuViewModel.IsDispensing = true;
        try
        {
            var manager = ArduinoSerialManager.Instance;
            if (!manager.IsConnected)
            {
                Console.WriteLine("[MainWindow] Arduino not connected");
                _cocktailsMenuViewModel.IsDispensing = false;
                return;
            }

            var channelDurations = _cocktailsMenuViewModel.MapIngredientsToChannels(cocktailVm);
            if (channelDurations.Count == 0)
            {
                Console.WriteLine($"[MainWindow] No matching ingredients for {cocktail.Title}");
                _cocktailsMenuViewModel.IsDispensing = false;
                return;
            }

            string ledColor = AppConfigService.Instance.Config.Modes
                .FirstOrDefault(m => m.Name.Equals(_modesViewModel.CurrentModeName, StringComparison.OrdinalIgnoreCase))
                ?.LedColor ?? "ORANGE";
            string command = ArduinoProtocolHelper.BuildActiveCommand(ledColor, channelDurations);
            Console.WriteLine($"[MainWindow] Sending command: {command}");

            bool success = manager.Send(command);
            if (!success)
            {
                Console.WriteLine("[MainWindow] Failed to send command");
                _cocktailsMenuViewModel.IsDispensing = false;
                return;
            }

            string? responseLine = manager.ReadLine();
            var response = ArduinoProtocolHelper.ParseActivateResponse(responseLine);
            switch (response)
            {
                case ActivateResponse.Success:
                    break;
                case ActivateResponse.ErrColor:
                    Console.WriteLine("[MainWindow] Arduino reported ERR ACTIVE COLOR (missing or invalid color)");
                    break;
                case ActivateResponse.ErrParams:
                    Console.WriteLine("[MainWindow] Arduino reported ERR ACTIVE PARAMS (no valid pump duration)");
                    break;
                case ActivateResponse.Ignored:
                    Console.WriteLine("[MainWindow] Arduino reported IGNORED ACTIVE (board not in WAIT state)");
                    break;
                case ActivateResponse.NoResponse:
                    Console.WriteLine("[MainWindow] No response from Arduino (timeout or command ignored in current state)");
                    break;
            }

            _cocktailsMenuViewModel.IsDispensing = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainWindow] Dispense error: {ex.Message}");
            _cocktailsMenuViewModel.IsDispensing = false;
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
