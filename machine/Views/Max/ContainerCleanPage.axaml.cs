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
            Bottle6, Bottle7, Bottle8, Bottle9, Bottle10
        ];
        ApplyLiquidAssignments();
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

    private void ApplyLiquidAssignments()
    {
        var assignments = AppConfigService.Instance.Config.LiquidAssignments;

        for (int i = 0; i < _bottles.Length; i++)
        {
            if (_bottles[i] == null) continue;
            bool hasLiquid = i < assignments.Length && !string.IsNullOrWhiteSpace(assignments[i]);
            _bottles[i]!.Tag = hasLiquid ? assignments[i] : $"Slot {i + 1}";
            _bottles[i]!.IsEnabled = hasLiquid;
        }
    }

    private void OnConfigChanged(object? sender, AppConfigChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(ApplyLiquidAssignments);
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
        await Task.Delay(delayMs);
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
}
