using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using DaevaMini.Config;
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

    private void OnBackArrow(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSettingsPage();
    }

    private async void OnFill(object? sender, RoutedEventArgs e)
    {
        var selectedChannels = new List<int>();
        for (int i = 0; i < _bottles.Length; i++)
        {
            if (_bottles[i] is { IsChecked: true })
                selectedChannels.Add(i + 1);
        }

        if (selectedChannels.Count == 0)
        {
            Console.WriteLine("[ContainerFillPage] No containers selected");
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
            await FinishFill(fillMs, selectedChannels);
            return;
        }

        string? responseLine = manager.ReadLine();
        var response = ArduinoProtocolHelper.ParseActivateResponse(responseLine);
        Console.WriteLine($"[ContainerFillPage] Response: {response}");

        await FinishFill(fillMs, selectedChannels);
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