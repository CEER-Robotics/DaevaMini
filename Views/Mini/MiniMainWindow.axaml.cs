using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DaevaMini.Config;
using DaevaMini.Controls;
using DaevaMini.Models;
using DaevaMini.Services;
using DaevaMini.ViewModels;

namespace DaevaMini.Views.Mini;

public partial class MiniMainWindow : Window
{
    private ContentControl? _pageContainer;
    private readonly ModesViewModel _modesViewModel = new();
    private readonly CocktailsMenuViewModel _cocktailsMenuViewModel;
    private UserControl? _cocktailMenuView;

    public MiniMainWindow()
    {
        InitializeComponent();
#if DEBUG
        Width = 1024;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
#else
        WindowState = WindowState.FullScreen;
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
#endif
        _pageContainer = this.FindControl<ContentControl>("PageContainer");
        _cocktailsMenuViewModel = new CocktailsMenuViewModel(_modesViewModel);
        ShowLockScreen();
    }

    public void ShowLockScreen()
    {
        if (_pageContainer == null) return;
        var lockScreen = new LockScreen(_modesViewModel);
        lockScreen.StartClicked += (_, _) => ShowCocktailsMenu();
        lockScreen.SettingsClicked += (_, _) => ShowSettings();
        _pageContainer.Content = lockScreen;
    }

    public void ShowCocktailsMenu()
    {
        if (_pageContainer == null) return;

        var repository = new MiniCocktailRepository();
        var menuViewModel = new CocktailMenuViewModel(repository, _modesViewModel)
        {
            SelectCocktailCommand = new RelayCommand(OnCocktailSelected)
        };

        var menu = new CocktailMenuMini { DataContext = menuViewModel };
        menu.BackClicked += (_, _) => ShowLockScreen();
        menu.SettingsClicked += (_, _) => ShowSettings();
        _cocktailMenuView = menu;
        _pageContainer.Content = menu;
    }

    public void ShowSettings()
    {
        if (_pageContainer == null) return;
        var settings = new SettingsMini(_modesViewModel);
        settings.BackClicked += (_, _) => ShowLockScreen();
        settings.FillClicked += (_, _) => ShowFillPage();
        settings.CleanClicked += (_, _) => ShowCleanPage();
        settings.EditModesClicked += (_, _) => ShowEditModes();
        _pageContainer.Content = settings;
    }

    public void ShowFillPage()
    {
        if (_pageContainer == null) return;
        var fillPage = new ChangeBottle(_modesViewModel);
        fillPage.BackClicked += (_, _) => ShowSettings();
        _pageContainer.Content = fillPage;
    }

    public void ShowCleanPage()
    {
        if (_pageContainer == null) return;
        var cleanPage = new MiniCleanPage(_modesViewModel);
        cleanPage.BackClicked += (_, _) => ShowSettings();
        _pageContainer.Content = cleanPage;
    }

    public void ShowEditModes()
    {
        if (_pageContainer == null) return;
        var editModes = new ChoseYourModes();
        editModes.BackClicked += (_, _) => ShowSettings();
        editModes.DoneClicked += (_, _) => ShowSettings();
        _pageContainer.Content = editModes;
    }

    private void OnCocktailSelected(object? parameter)
    {
        if (parameter is not Cocktail cocktail) return;
        var card = new CocktailCard
        {
            DataContext = cocktail,
            Width = 1920,
            Height = 1080
        };
        card.CloseClicked += OnCardCloseClicked;
        card.DaleClicked += OnCardDaleClicked;
        if (_pageContainer != null)
            _pageContainer.Content = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = card
            };
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
            Console.WriteLine($"[MiniMainWindow] No matching cocktail config for: {cocktail.Title}");
            return;
        }

        _cocktailsMenuViewModel.IsDispensing = true;
        try
        {
            var manager = ArduinoSerialManager.Instance;
            if (!manager.IsConnected)
            {
                Console.WriteLine("[MiniMainWindow] Arduino not connected");
                _cocktailsMenuViewModel.IsDispensing = false;
                return;
            }

            var channelDurations = _cocktailsMenuViewModel.MapIngredientsToChannels(cocktailVm);
            if (channelDurations.Count == 0)
            {
                Console.WriteLine($"[MiniMainWindow] No matching ingredients for {cocktail.Title}");
                _cocktailsMenuViewModel.IsDispensing = false;
                return;
            }

            string command = cocktail.LedRgb is { } rgb
                ? ArduinoProtocolHelper.BuildActiveCommand(rgb, channelDurations)
                : ArduinoProtocolHelper.BuildActiveCommand(
                    AppConfigService.Instance.Config.Modes
                        .FirstOrDefault(m => m.Name.Equals(_modesViewModel.CurrentModeName, StringComparison.OrdinalIgnoreCase))
                        ?.LedColor ?? "ORANGE",
                    channelDurations);
            Console.WriteLine($"[MiniMainWindow] Sending command: {command}");

            bool success = manager.Send(command);
            if (!success)
            {
                Console.WriteLine("[MiniMainWindow] Failed to send command");
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
                    Console.WriteLine("[MiniMainWindow] Arduino reported ERR ACTIVE COLOR");
                    break;
                case ActivateResponse.ErrParams:
                    Console.WriteLine("[MiniMainWindow] Arduino reported ERR ACTIVE PARAMS");
                    break;
                case ActivateResponse.Ignored:
                    Console.WriteLine("[MiniMainWindow] Arduino reported IGNORED ACTIVE");
                    break;
                case ActivateResponse.NoResponse:
                    Console.WriteLine("[MiniMainWindow] No response from Arduino");
                    break;
            }

            _cocktailsMenuViewModel.IsDispensing = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MiniMainWindow] Dispense error: {ex.Message}");
            _cocktailsMenuViewModel.IsDispensing = false;
        }
    }
}
