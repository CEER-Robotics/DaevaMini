using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DaevaMini.Config;
using DaevaMini.Services;
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
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();
            bool isMini = args.Contains("--Mini", StringComparer.OrdinalIgnoreCase);

            AppConfigService.Instance.LoadConfig(isMini ? "appsettings.mini.yaml" : "appsettings.max.yaml");

            desktop.MainWindow = isMini
                ? new MiniMainWindow()
                : new MainWindow();

            desktop.Exit += OnExit;
            ArduinoSerialManager.Instance.Initialize();
            MachineSyncService.Instance.Start(AppConfigService.Instance.CurrentProfileKey);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        MachineSyncService.Instance.Stop();
        ArduinoSerialManager.Instance.Dispose();
    }
}
