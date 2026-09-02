using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DaevaMini.Config;
using DaevaMini.Services;

namespace DaevaMini.Views.Max;

public partial class SettingsDaevaMax : UserControl
{
    public SettingsDaevaMax()
    {
        InitializeComponent();
        ApplyEventMode();
    }

    private static readonly IBrush SelectedBackground = new SolidColorBrush(Color.Parse("#CAF0F8"));
    private static readonly IBrush SelectedForeground = new SolidColorBrush(Color.Parse("#131818"));

    // Track/thumb colors and the two thumb positions for the "Attiva fusti" switch.
    private static readonly IBrush SwitchTrackOff = new SolidColorBrush(Color.Parse("#33FFFFFF"));
    private static readonly IBrush SwitchThumbOff = new SolidColorBrush(Color.Parse("#E7E6DC"));
    private static readonly Thickness ThumbOffMargin = new(4, 4, 4, 4);
    private static readonly Thickness ThumbOnMargin = new(42, 4, 4, 4);

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSplash();
    }

    private void OnLiquidsClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowLiquidSetupPage();
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

    private void OnCocktailsClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowCocktailWorkshopPage();
    }

    private void OnChangePinClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowChangePinPage();
    }

    private void OnFlowRateClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowFlowRatePage();
    }

    private void OnEventModeToggle(object? sender, RoutedEventArgs e)
    {
        bool large = AppConfigService.Instance.Config.IsLargeEvent;
        SetEventMode(large ? "Small" : "Large");
    }

    /// <summary>
    /// Switches the machine between bottles only and bottles plus kegs. Everything that
    /// pours reads AppConfig.GetActiveLiquidAssignments(), so this single flag is enough.
    /// </summary>
    private void SetEventMode(string mode)
    {
        var config = AppConfigService.Instance.Config;
        if (config.EventMode.Equals(mode, StringComparison.OrdinalIgnoreCase))
            return;

        config.EventMode = mode;
        AppConfigService.Instance.SaveConfig("event-mode");
        MachineWarningService.Instance.Refresh();
        ApplyEventMode();
    }

    private void ApplyEventMode()
    {
        bool large = AppConfigService.Instance.Config.IsLargeEvent;

        EventModeTrack.Background = large ? SelectedBackground : SwitchTrackOff;
        EventModeThumb.Background = large ? SelectedForeground : SwitchThumbOff;
        EventModeThumb.Margin = large ? ThumbOnMargin : ThumbOffMargin;

        // Same switch as before under a plainer name: "large event" only ever meant
        // "the kegs are plugged in", and saying so directly saves explaining it.
        EventModeDetail.Text = large
            ? "Fusti collegati: le pompe con lo stesso liquido restano ferme."
            : "Fusti scollegati: si usano solo le bottiglie.";
    }
}
