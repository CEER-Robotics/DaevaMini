using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DaevaMini.Config;
using DaevaMini.Controls;
using DaevaMini.Models;
using DaevaMini.Services;
using DaevaMini.Views.Max;
using DaevaMini.ViewModels;

namespace DaevaMini;

public partial class MainWindow : Window
{
    private ContentControl? _pageContainer;
    private UserControl? _cocktailMenuView;
    private bool _isDispensing;

    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        Width = 1920;
        Height = 1080;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
#else
        WindowState = WindowState.FullScreen;
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
#endif
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
        if (_pageContainer == null) return;

        var repository = new MaxCocktailRepository();
        var menuViewModel = new CocktailMenuViewModel(repository)
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

    private async void OnCardDaleClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not CocktailCard card || card.DataContext is not Cocktail cocktail)
            return;
        if (_isDispensing) return;

        string profileKey = AppConfigService.Instance.CurrentProfileKey;
        using var runtimeOperation = MachineRuntimeState.Instance.BeginOperation("dispense");

        if (cocktail.Ingredients.Count == 0)
        {
            Console.WriteLine($"[MainWindow] No ingredients defined for: {cocktail.Title}");
            MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, null, null, new
            {
                reason = "no-ingredients"
            });
            return;
        }

        _isDispensing = true;
        try
        {
            var manager = ArduinoSerialManager.Instance;

            var config = AppConfigService.Instance.Config;
            var channelDurations = ArduinoProtocolHelper.MapIngredientsToChannels(
                cocktail.Ingredients, config.LiquidAssignments, config.FlowRate.MillisecondsPerMilliliter);
            if (channelDurations.Count == 0)
            {
                Console.WriteLine($"[MainWindow] No matching ingredients for {cocktail.Title}");
                MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, null, channelDurations, new
                {
                    reason = "no-channel-mapping"
                });
                return;
            }

            string command = cocktail.LedRgb is { } rgb
                ? ArduinoProtocolHelper.BuildActiveCommand(rgb, channelDurations)
                : ArduinoProtocolHelper.BuildActiveCommand("ORANGE", channelDurations);
            Console.WriteLine($"[MainWindow] Sending command: {command}");

            if (!manager.IsConnected)
            {
                Console.WriteLine("[MainWindow] Arduino not connected");
#if DEBUG
                int totalDurationDebug = 0;
                foreach (var ms in channelDurations.Values)
                    if (ms > totalDurationDebug) totalDurationDebug = ms;
                Console.WriteLine($"[MainWindow] DEBUG: simulating dispense for {totalDurationDebug}ms");
                await card.StartDispensing(totalDurationDebug);
                MachineTelemetryService.Instance.RecordDispenseEvent(
                    profileKey,
                    "completed",
                    cocktail,
                    null,
                    channelDurations,
                    new { simulated = true });
#else
                MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, null, channelDurations, new
                {
                    reason = "arduino-not-connected"
                });
#endif
                return;
            }

            if (!manager.TrySendActive(command, out ActivateResponse response))
            {
                Console.WriteLine("[MainWindow] Failed to send command");
                MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, null, channelDurations, new
                {
                    reason = "send-failed"
                });
                return;
            }

            Console.WriteLine($"[MainWindow] Arduino response: {response}");

            int totalDuration = 0;
            foreach (var ms in channelDurations.Values)
                if (ms > totalDuration) totalDuration = ms;
            var dispensingTask = card.StartDispensing(totalDuration);
            if (response != ActivateResponse.Success)
            {
                await dispensingTask;
                MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, null, channelDurations, new
                {
                    reason = "arduino-response",
                    response = response.ToString()
                });
                return;
            }

            await dispensingTask;
            MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "completed", cocktail, null, channelDurations);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainWindow] Dispense error: {ex.Message}");
            MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, null, null, new
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

    public void ShowSettingsPage()
    {
        if (_pageContainer != null)
        {
            _pageContainer.Content = new SettingsDaevaMax();
        }
    }

    public void ShowSettingsUnlockPage()
    {
        if (_pageContainer != null)
        {
            _pageContainer.Content = new NumberPadPage();
        }
    }

    public void ShowContainerSetupPage()
    {
        if (_pageContainer != null)
        {
            _pageContainer.Content = new ContainerSetupPage();
        }
    }

    public void ShowContainerFillPage()
    {
        if (_pageContainer != null)
        {
            _pageContainer.Content = new ContainerFillPage();
        }
    }

    public void ShowContainerCleanPage()
    {
        if (_pageContainer != null)
        {
            _pageContainer.Content = new ContainerCleanPage();
        }
    }
}
