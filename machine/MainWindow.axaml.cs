using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
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

    // The machine's panel. Pages position everything with absolute Canvas
    // coordinates against this size, so it must not change.
    private const double DesignWidth = 1920;
    private const double DesignHeight = 1080;

    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        // Dev preview: keep the 1920x1080 design and scale the window down to fit,
        // rather than letting a smaller screen crop it. The screen is only known
        // once the window is open, so the sizing happens in OnOpened.
        Width = DesignWidth;
        Height = DesignHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
#else
        // On the machine the window is exactly 1920x1080, so scaling would be a
        // no-op anyway - disable it and keep the original rendering path.
        RootScaler.Stretch = Stretch.None;
        WindowState = WindowState.FullScreen;
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
#endif
        _pageContainer = this.FindControl<ContentControl>("PageContainer");
        ShowSplash();
    }

#if DEBUG
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitPreviewToScreen();
    }

    // Shrink the window until the whole 1920x1080 design fits on this screen, so the
    // preview shows the same framing as the machine's panel instead of a crop.
    // RootScaler does the actual scaling; no design coordinate is touched.
    private void FitPreviewToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        // WorkingArea is in physical pixels; Width/Height are logical units.
        double availableWidth = screen.WorkingArea.Width / screen.Scaling;
        double availableHeight = screen.WorkingArea.Height / screen.Scaling;

        // Width/Height cover the whole window, so keep the frame out of the maths.
        double frameWidth = Width - ClientSize.Width;
        double frameHeight = Height - ClientSize.Height;

        double scale = Math.Min(
            (availableWidth - frameWidth) / DesignWidth,
            (availableHeight - frameHeight) / DesignHeight);

        if (scale >= 1 || scale <= 0) return;

        Width = Math.Floor(DesignWidth * scale) + frameWidth;
        Height = Math.Floor(DesignHeight * scale) + frameHeight;

        Position = new PixelPoint(
            screen.WorkingArea.X + (int)((screen.WorkingArea.Width - Width * screen.Scaling) / 2),
            screen.WorkingArea.Y + (int)((screen.WorkingArea.Height - Height * screen.Scaling) / 2));
    }
#endif

    public void ShowSplash()
    {
        if (_pageContainer != null)
        {
            SetPage(new SplashPage());
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
        menu.PourHandler = DispenseAsync;
        menu.TapHandler = OpenTap;
        _cocktailMenuView = menu;
        SetPage(menu);
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
            SetPage(card);
    }

    private void OnCardCloseClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is CocktailCard card)
        {
            card.CloseClicked -= OnCardCloseClicked;
            card.DaleClicked -= OnCardDaleClicked;
        }
        if (_cocktailMenuView != null && _pageContainer != null)
            SetPage(_cocktailMenuView);
    }

    private async void OnCardDaleClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not CocktailCard card || card.DataContext is not Cocktail cocktail)
            return;

        await DispenseAsync(cocktail, card.StartDispensing);
    }

    /// <summary>
    /// Runs a dispense for the given cocktail. <paramref name="showProgress"/> renders the
    /// progress for the requested duration, so the caller decides where it appears: the
    /// detail card or a card in the menu grid.
    /// </summary>
    /// <summary>Subtracts the poured millilitres from the bottle on each line.</summary>
    private static void RecordBottleUsage(Cocktail cocktail, AppConfig config)
    {
        var perChannel = new Dictionary<int, int>();
        var assignments = config.GetActiveLiquidAssignments();
        foreach (var ingredient in cocktail.Ingredients)
        {
            for (int index = 0; index < assignments.Length; index++)
            {
                if (!assignments[index].Equals(ingredient.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                int channel = index + 1;
                perChannel[channel] = perChannel.GetValueOrDefault(channel) + ingredient.Milliliters;
                break;
            }
        }

        MachineWarningService.Instance.RecordConsumption(perChannel);
    }

    /// <returns>True when the drink was actually poured, so the caller can confirm it to the guest.</returns>
    private async Task<bool> DispenseAsync(Cocktail cocktail, Func<int, Task> showProgress)
    {
        if (_isDispensing) return false;

        string profileKey = AppConfigService.Instance.CurrentProfileKey;
        using var runtimeOperation = MachineRuntimeState.Instance.BeginOperation("dispense");

        if (cocktail.Ingredients.Count == 0)
        {
            Console.WriteLine($"[MainWindow] No ingredients defined for: {cocktail.Title}");
            MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, null, null, new
            {
                reason = "no-ingredients"
            });
            return false;
        }

        _isDispensing = true;
        try
        {
            var manager = ArduinoSerialManager.Instance;

            var config = AppConfigService.Instance.Config;
            var channelDurations = ArduinoProtocolHelper.MapIngredientsToChannels(
                cocktail.Ingredients, config.GetActiveLiquidAssignments(), config.GetFlowRateMsPerMl);
            if (channelDurations.Count == 0)
            {
                Console.WriteLine($"[MainWindow] No matching ingredients for {cocktail.Title}");
                MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, null, channelDurations, new
                {
                    reason = "no-channel-mapping"
                });
                return false;
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
                await showProgress(totalDurationDebug);
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
                return false;
#endif
                return true;
            }

            if (!manager.TrySendActive(command, out ActivateResponse response))
            {
                Console.WriteLine("[MainWindow] Failed to send command");
                MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, null, channelDurations, new
                {
                    reason = "send-failed"
                });
                return false;
            }

            Console.WriteLine($"[MainWindow] Arduino response: {response}");

            int totalDuration = 0;
            foreach (var ms in channelDurations.Values)
                if (ms > totalDuration) totalDuration = ms;
            var dispensingTask = showProgress(totalDuration);
            if (response != ActivateResponse.Success)
            {
                await dispensingTask;
                MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "failed", cocktail, null, channelDurations, new
                {
                    reason = "arduino-response",
                    response = response.ToString()
                });
                return false;
            }

            await dispensingTask;
            MachineTelemetryService.Instance.RecordDispenseEvent(profileKey, "completed", cocktail, null, channelDurations);
            RecordBottleUsage(cocktail, config);
            return true;
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

        return false;
    }

    // Orange while the settings screens are open, per the machine's visual language.
    private static readonly (byte R, byte G, byte B) SettingsTint = (255, 120, 0);

    /// <summary>
    /// Swaps the visible page and tells the strips which part of the machine we are
    /// in. Every navigation goes through here, so no screen can forget to do it.
    /// </summary>
    private void SetPage(object page)
    {
        if (_pageContainer == null) return;

        _pageContainer.Content = page;
        ApplyIdleTint(page);
    }

    /// <summary>
    /// Holds an orange tint for the whole settings area and the default idle color
    /// everywhere else. Fire-and-forget on a worker thread: the tint is decoration and
    /// the UI must not wait on the serial port to change page. The firmware's "OK TINT"
    /// is left on the wire for <see cref="ArduinoSerialManager"/> to skip, which it
    /// already does for unsolicited lines.
    /// </summary>
    private static void ApplyIdleTint(object page)
    {
        bool isSettings = page is SettingsDaevaMax or NumberPadPage or DosesPage
            or FlowRatePage or ContainerSetupPage or ContainerFillPage
            or ContainerCleanPage;

        string command = isSettings
            ? ArduinoProtocolHelper.BuildTintCommand(SettingsTint)
            : ArduinoProtocolHelper.TintOffCommand;

        Task.Run(() => ArduinoSerialManager.Instance.Send(command));
    }

    /// <summary>
    /// Opens the tap for a drink and hands back the live session, or null when it
    /// cannot start. The caller owns the session and must dispose it to close.
    /// </summary>
    private static TapSession? OpenTap(Cocktail cocktail)
    {
        var config = AppConfigService.Instance.Config;
        var channels = ArduinoProtocolHelper.MapIngredientsToChannels(
            cocktail.Ingredients, config.GetActiveLiquidAssignments(), config.GetFlowRateMsPerMl);

        // A tap drink is one ingredient on one line; the dose in the config is only
        // there to describe the drink, since the guest decides how much to draw.
        foreach (int channel in channels.Keys)
            return TapSession.TryOpen(cocktail, channel);

        Console.WriteLine($"[MainWindow] No channel mapped for tap drink {cocktail.Title}");
        return null;
    }

    public void ShowDosesPage()
    {
        if (_pageContainer != null)
            SetPage(new DosesPage());
    }

    public void ShowFlowRatePage()
    {
        if (_pageContainer != null)
            SetPage(new FlowRatePage());
    }

    public void ShowSettingsPage()
    {
        if (_pageContainer != null)
        {
            SetPage(new SettingsDaevaMax());
        }
    }

    public void ShowSettingsUnlockPage()
    {
        if (_pageContainer != null)
        {
            SetPage(new NumberPadPage());
        }
    }

    public void ShowContainerSetupPage()
    {
        if (_pageContainer != null)
        {
            SetPage(new ContainerSetupPage());
        }
    }

    public void ShowContainerFillPage()
    {
        if (_pageContainer != null)
        {
            SetPage(new ContainerFillPage());
        }
    }

    public void ShowContainerCleanPage()
    {
        if (_pageContainer != null)
        {
            SetPage(new ContainerCleanPage());
        }
    }
}
