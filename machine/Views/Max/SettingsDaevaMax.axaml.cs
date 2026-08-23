using System;
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
    private static readonly IBrush IdleBackground = new SolidColorBrush(Color.Parse("#15FFFFFF"));
    private static readonly IBrush SelectedForeground = new SolidColorBrush(Color.Parse("#131818"));
    private static readonly IBrush IdleForeground = new SolidColorBrush(Color.Parse("#8A8F80"));

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

    private void OnDosesClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowDosesPage();
    }

    private void OnFlowRateClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowFlowRatePage();
    }

    private void OnSmallEventClick(object? sender, RoutedEventArgs e) => SetEventMode("Small");

    private void OnLargeEventClick(object? sender, RoutedEventArgs e) => SetEventMode("Large");

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

        SmallEventBg.Background = large ? IdleBackground : SelectedBackground;
        SmallEventText.Foreground = large ? IdleForeground : SelectedForeground;
        LargeEventBg.Background = large ? SelectedBackground : IdleBackground;
        LargeEventText.Foreground = large ? SelectedForeground : IdleForeground;

        // Same switch as before under a plainer name: "large event" only ever meant
        // "the kegs are plugged in", and saying so directly saves explaining it.
        EventModeDetail.Text = large
            ? "Fusti collegati: le pompe con lo stesso liquido restano ferme."
            : "Fusti scollegati: si usano solo le bottiglie.";
    }
}
