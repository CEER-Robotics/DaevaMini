using System;
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
    private UserControl? _cocktailMenuView;
    private bool _isDispensing;

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

    private async void OnCardDaleClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not CocktailCard card || card.DataContext is not Cocktail cocktail)
            return;
        if (_isDispensing) return;

        string profileKey = AppConfigService.Instance.CurrentProfileKey;
        using var runtimeOperation = MachineRuntimeState.Instance.BeginOperation("dispense");

        if (cocktail.Ingredients.Count == 0)
        {
            Console.WriteLine($"[MiniMainWindow] No ingredients defined for: {cocktail.Title}");
            MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, _modesViewModel.CurrentMode.Name, null, new
            {
                reason = "no-ingredients"
            });
            return;
        }

        _isDispensing = true;
        try
        {
            var manager = ArduinoSerialManager.Instance;

            var mode = _modesViewModel.CurrentMode;
            int msPerMl = AppConfigService.Instance.Config.FlowRate.MillisecondsPerMilliliter;
            var channelDurations = ArduinoProtocolHelper.MapIngredientsToChannels(cocktail.Ingredients, mode.LiquidAssignments, msPerMl);
            if (channelDurations.Count == 0)
            {
                Console.WriteLine($"[MiniMainWindow] No matching ingredients for {cocktail.Title}");
                MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, mode.Name, channelDurations, new
                {
                    reason = "no-channel-mapping"
                });
                return;
            }

            string command = cocktail.LedRgb is { } rgb
                ? ArduinoProtocolHelper.BuildActiveCommand(rgb, channelDurations)
                : ArduinoProtocolHelper.BuildActiveCommand(mode.LedColor, channelDurations);
            Console.WriteLine($"[MiniMainWindow] Sending command: {command}");

            if (!manager.IsConnected)
            {
                Console.WriteLine("[MiniMainWindow] Arduino not connected");
                MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, mode.Name, channelDurations, new
                {
                    reason = "arduino-not-connected"
                });
#if DEBUG
                int totalDurationDebug = 0;
                foreach (var ms in channelDurations.Values)
                    if (ms > totalDurationDebug) totalDurationDebug = ms;
                Console.WriteLine($"[MiniMainWindow] DEBUG: simulating dispense for {totalDurationDebug}ms");
                await card.StartDispensing(totalDurationDebug);
#endif
                return;
            }

            if (!manager.Send(command))
            {
                Console.WriteLine("[MiniMainWindow] Failed to send command");
                MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, mode.Name, channelDurations, new
                {
                    reason = "send-failed"
                });
                return;
            }

            int totalDuration = 0;
            foreach (var ms in channelDurations.Values)
                if (ms > totalDuration) totalDuration = ms;
            var dispensingTask = card.StartDispensing(totalDuration);

            var response = ArduinoProtocolHelper.ParseActivateResponse(manager.ReadLine());
            Console.WriteLine($"[MiniMainWindow] Arduino response: {response}");
            if (response != ActivateResponse.Success)
            {
                await dispensingTask;
                MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, mode.Name, channelDurations, new
                {
                    reason = "arduino-response",
                    response = response.ToString()
                });
                return;
            }

            await dispensingTask;
            MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "completed", cocktail, mode.Name, channelDurations);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MiniMainWindow] Dispense error: {ex.Message}");
            MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, _modesViewModel.CurrentMode.Name, null, new
            {
                reason = "exception",
                ex.Message
            });
        }
        finally
        {
            _isDispensing = false;
        }
    }

}
