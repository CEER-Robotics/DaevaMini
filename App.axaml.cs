using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DaevaMini.Config;

namespace DaevaMini;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Load configuration first
        AppConfigService.Instance.LoadConfig();
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            
            // Initialize Arduino connection when app starts
            desktop.Exit += OnExit;
            ArduinoSerialManager.Instance.Initialize();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        // Clean up Arduino connection when app exits
        ArduinoSerialManager.Instance.Dispose();
    }
}