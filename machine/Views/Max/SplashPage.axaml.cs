using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DaevaMini.Views.Max;

public partial class SplashPage : UserControl
{
    private readonly DispatcherTimer _clockTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    public SplashPage()
    {
        InitializeComponent();
        UpdateClock();
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
    }

    public void OnTouchToStart(object? sender, RoutedEventArgs e)
    {
        // Normal navigation
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowCocktailsMenu();
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSettingsUnlockPage();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _clockTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm");
    }
}
