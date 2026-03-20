using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DaevaMini.Config;
using DaevaMini.Views.Mini;

namespace DaevaMini;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppConfigService.Instance.LoadConfig();
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();
            bool isMini = args.Contains("--Mini", StringComparer.OrdinalIgnoreCase);

            desktop.MainWindow = isMini
                ? new MiniMainWindow()
                : new MainWindow();

            desktop.Exit += OnExit;
            ArduinoSerialManager.Instance.Initialize();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        ArduinoSerialManager.Instance.Dispose();
    }
}