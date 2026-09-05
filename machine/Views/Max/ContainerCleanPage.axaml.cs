using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DaevaMini.Config;
using DaevaMini.Services;
namespace DaevaMini.Views.Max;

public partial class ContainerCleanPage : UserControl
{
    private readonly ToggleButton?[] _bottles;

    public ContainerCleanPage()
    {
        InitializeComponent();
        _bottles =
        [
            Bottle1, Bottle2, Bottle3, Bottle4, Bottle5,
            Bottle6, Bottle7, Bottle8, Bottle9, Bottle10,
            Keg1, Keg2, Keg3, Keg4
        ];
        ApplyLiquidAssignments();
        ApplyKegAvailability();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        AppConfigService.Instance.ConfigChanged += OnConfigChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        AppConfigService.Instance.ConfigChanged -= OnConfigChanged;
        base.OnUnloaded(e);
    }

    /// <summary>
    /// Rinsing is about the plumbing, not the recipe: a line is flushed with the bottle
    /// already pulled and its tube in water, so what used to be in it is beside the
    /// point and its name would be actively misleading. Lines are labelled by number,
    /// and all of them can be rinsed whether or not a liquid is assigned - an empty
    /// line is the normal thing to want to rinse.
    /// </summary>
    private void ApplyLiquidAssignments()
    {
        bool kegsOn = AppConfigService.Instance.Config.IsLargeEvent;

        for (int i = 0; i < _bottles.Length; i++)
        {
            if (_bottles[i] == null) continue;

            // A blocked keg carries no caption: the red overlay's own message takes that
            // spot, and leaving "Fusto 11" there would print two labels on top of each
            // other. Same rule as the container page.
            if (i >= AppConfig.PumpChannelCount && !kegsOn)
            {
                _bottles[i]!.Tag = string.Empty;
                _bottles[i]!.IsEnabled = false;
                continue;
            }

            _bottles[i]!.Tag = LineName(i + 1);
            _bottles[i]!.IsEnabled = true;
        }
    }

    /// <summary>Channels 1-10 are the bottle pumps, 11-14 the pressurised kegs.</summary>
    private static string LineName(int channel)
        => channel > AppConfig.PumpChannelCount ? $"Fusto {channel}" : $"Linea {channel}";

    private void OnConfigChanged(object? sender, AppConfigChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyLiquidAssignments();
            ApplyKegAvailability();
        });
    }

    private void OnBackArrow(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSettingsPage();
    }

    private async void OnClean(object? sender, RoutedEventArgs e)
    {
        string profileKey = AppConfigService.Instance.CurrentProfileKey;
        using var runtimeOperation = MachineRuntimeState.Instance.BeginOperation("clean");
        var selectedChannels = new List<int>();
        for (int i = 0; i < _bottles.Length; i++)
        {
            if (_bottles[i] is { IsChecked: true })
                selectedChannels.Add(i + 1);
        }

        if (selectedChannels.Count == 0)
        {
            Console.WriteLine("[ContainerCleanPage] No containers selected");
            MachineTelemetryService.Instance.RecordMaintenanceEvent(profileKey, "clean", "failed", selectedChannels, 0, new
            {
                reason = "no-channels-selected"
            });
            return;
        }

        var config = AppConfigService.Instance.Config;
        int cleanMs = config.CleanDurationMs;
        var channelDurations = selectedChannels.ToDictionary(ch => ch, _ => cleanMs);

        CleanBtn.IsEnabled = false;

        Console.WriteLine($"[ContainerCleanPage] Cleaning channels: {string.Join(", ", selectedChannels)} for {cleanMs}ms each");

        var manager = ArduinoSerialManager.Instance;
        if (!manager.IsConnected)
        {
            Console.WriteLine("[ContainerCleanPage] Arduino not connected");
            MachineTelemetryService.Instance.RecordMaintenanceEvent(profileKey, "clean", "failed", selectedChannels, cleanMs, new
            {
                reason = "arduino-not-connected"
            });
            await FinishClean(cleanMs, selectedChannels);
            return;
        }

        string command = ArduinoProtocolHelper.BuildActiveCommand(
            "BLUE",
            channelDurations);
        Console.WriteLine($"[ContainerCleanPage] Sending: {command}");

        bool sent = manager.TrySendActive(command, out ActivateResponse response);
        if (!sent)
        {
            Console.WriteLine("[ContainerCleanPage] Failed to send command");
            MachineTelemetryService.Instance.RecordMaintenanceEvent(profileKey, "clean", "failed", selectedChannels, cleanMs, new
            {
                reason = "send-failed"
            });
            await FinishClean(cleanMs, selectedChannels);
            return;
        }

        Console.WriteLine($"[ContainerCleanPage] Response: {response}");
        if (response != ActivateResponse.Success)
        {
            MachineTelemetryService.Instance.RecordMaintenanceEvent(profileKey, "clean", "failed", selectedChannels, cleanMs, new
            {
                reason = "arduino-response",
                response = response.ToString()
            });
            await FinishClean(cleanMs, selectedChannels);
            return;
        }

        await FinishClean(cleanMs, selectedChannels);
        MachineTelemetryService.Instance.RecordMaintenanceEvent(profileKey, "clean", "completed", selectedChannels, cleanMs);
    }

    private async Task FinishClean(int delayMs, List<int> cleanedChannels)
    {
        await ShowCleanProgress(delayMs, cleanedChannels);
        foreach (int ch in cleanedChannels)
        {
            int idx = ch - 1;
            if (idx >= 0 && idx < _bottles.Length && _bottles[idx] != null)
            {
                _bottles[idx]!.IsChecked = false;
                if (!_bottles[idx]!.Classes.Contains("Cleaned"))
                    _bottles[idx]!.Classes.Add("Cleaned");
            }
        }
        CleanBtn.IsEnabled = true;
    }

    /// <summary>
    /// Marks the kegs as unusable while the machine runs as a small event: they stay
    /// visible, crossed out, so it is clear they exist but are not connected right now.
    /// </summary>
    private void ApplyKegAvailability()
    {
        bool large = AppConfigService.Instance.Config.IsLargeEvent;
        Keg1Blocked.IsVisible = !large;
        Keg2Blocked.IsVisible = !large;
        Keg3Blocked.IsVisible = !large;
        Keg4Blocked.IsVisible = !large;
    }

    /// <summary>Runs the wait as a visible progress bar rather than a frozen screen.</summary>
    private async Task ShowCleanProgress(int durationMs, List<int> channels)
    {
        CleanProgressDetail.Text = string.Join("   ·   ", channels.Select(LineName));

        CleanProgress.Value = 0;
        CleanProgressOverlay.IsVisible = true;
        try
        {
            const int updateIntervalMs = 50;
            int elapsed = 0;
            while (elapsed < durationMs)
            {
                await Task.Delay(updateIntervalMs);
                elapsed += updateIntervalMs;
                CleanProgress.Value = Math.Min(100.0 * elapsed / durationMs, 100);
            }
        }
        finally
        {
            CleanProgressOverlay.IsVisible = false;
        }
    }
}
