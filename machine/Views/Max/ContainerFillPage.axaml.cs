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

public partial class ContainerFillPage : UserControl
{
    private readonly ToggleButton?[] _bottles;

    public ContainerFillPage()
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

    private async void OnFill(object? sender, RoutedEventArgs e)
    {
        string profileKey = AppConfigService.Instance.CurrentProfileKey;
        using var runtimeOperation = MachineRuntimeState.Instance.BeginOperation("fill");
        var selectedChannels = new List<int>();
        for (int i = 0; i < _bottles.Length; i++)
        {
            if (_bottles[i] is { IsChecked: true })
                selectedChannels.Add(i + 1);
        }

        if (selectedChannels.Count == 0)
        {
            Console.WriteLine("[ContainerFillPage] No containers selected");
            MachineTelemetryService.Instance.RecordMaintenanceEvent(profileKey, "fill", "failed", selectedChannels, 0, new
            {
                reason = "no-channels-selected"
            });
            return;
        }

        var config = AppConfigService.Instance.Config;
        int fillMs = config.FillDurationMs;
        var channelDurations = selectedChannels.ToDictionary(ch => ch, _ => fillMs);

        FillBtn.IsEnabled = false;

        Console.WriteLine($"[ContainerFillPage] Filling channels: {string.Join(", ", selectedChannels)} for {fillMs}ms each");

        var manager = ArduinoSerialManager.Instance;
        if (!manager.IsConnected)
        {
            Console.WriteLine("[ContainerFillPage] Arduino not connected");
            MachineTelemetryService.Instance.RecordMaintenanceEvent(profileKey, "fill", "failed", selectedChannels, fillMs, new
            {
                reason = "arduino-not-connected"
            });
            await FinishFill(fillMs, selectedChannels);
            return;
        }

        string command = ArduinoProtocolHelper.BuildActiveCommand(
            "ORANGE",
            channelDurations);
        Console.WriteLine($"[ContainerFillPage] Sending: {command}");

        bool sent = manager.Send(command);
        if (!sent)
        {
            Console.WriteLine("[ContainerFillPage] Failed to send command");
            MachineTelemetryService.Instance.RecordMaintenanceEvent(profileKey, "fill", "failed", selectedChannels, fillMs, new
            {
                reason = "send-failed"
            });
            await FinishFill(fillMs, selectedChannels);
            return;
        }

        string? responseLine = manager.ReadLine();
        var response = ArduinoProtocolHelper.ParseActivateResponse(responseLine);
        Console.WriteLine($"[ContainerFillPage] Response: {response}");
        if (response != ActivateResponse.Success)
        {
            MachineTelemetryService.Instance.RecordMaintenanceEvent(profileKey, "fill", "failed", selectedChannels, fillMs, new
            {
                reason = "arduino-response",
                response = response.ToString()
            });
            await FinishFill(fillMs, selectedChannels);
            return;
        }

        await FinishFill(fillMs, selectedChannels);
        MachineTelemetryService.Instance.RecordMaintenanceEvent(profileKey, "fill", "completed", selectedChannels, fillMs);
    }

    private async Task FinishFill(int delayMs, List<int> filledChannels)
    {
        await Task.Delay(delayMs);
        foreach (int ch in filledChannels)
        {
            int idx = ch - 1;
            if (idx >= 0 && idx < _bottles.Length && _bottles[idx] != null)
            {
                _bottles[idx]!.IsChecked = false;
                if (!_bottles[idx]!.Classes.Contains("Filled"))
                    _bottles[idx]!.Classes.Add("Filled");
            }
        }
        FillBtn.IsEnabled = true;
    }
}
