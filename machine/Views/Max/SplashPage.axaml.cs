using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
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
        UpdateStatus();
        _clockTimer.Tick += (_, _) =>
        {
            UpdateClock();
            UpdateStatus();
        };
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

    /// <summary>
    /// Mirrors the serial link in the status badge. ArduinoSerialManager exposes no
    /// change notification, so the badge rides the one second clock tick.
    /// </summary>
    private void UpdateStatus()
    {
        bool connected = ArduinoSerialManager.Instance.IsConnected;
        StatusDot.Fill = new SolidColorBrush(Color.Parse(connected ? "#10B981" : "#F59E0B"));
        StatusText.Text = connected ? "PRONTA" : "NON CONNESSA";
    }
}
