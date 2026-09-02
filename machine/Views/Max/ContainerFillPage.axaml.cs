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
    private List<int> _pendingChannels = new();

    /// <summary>Channels above this are the pressurised kegs, not the bottle pumps.</summary>
    private const int PumpChannels = 10;

    public ContainerFillPage()
    {
        InitializeComponent();
        // Index is channel - 1: ten pumps, then the four pressurised kegs on P11-P14.
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

    private void ApplyLiquidAssignments()
    {
        var assignments = AppConfigService.Instance.Config.GetActiveLiquidAssignments();
        bool kegsOn = AppConfigService.Instance.Config.IsLargeEvent;

        for (int i = 0; i < _bottles.Length; i++)
        {
            if (_bottles[i] == null) continue;

            // A blocked keg carries no caption: the red overlay's own message takes that
            // spot, and leaving "Slot 11" there would print two labels on top of each other.
            if (i >= PumpChannels && !kegsOn)
            {
                _bottles[i]!.Tag = string.Empty;
                _bottles[i]!.IsEnabled = false;
                continue;
            }

            bool hasLiquid = i < assignments.Length && !string.IsNullOrWhiteSpace(assignments[i]);
            _bottles[i]!.Tag = hasLiquid ? assignments[i] : $"Slot {i + 1}";
            _bottles[i]!.IsEnabled = hasLiquid;
        }
    }

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

    private void OnFill(object? sender, RoutedEventArgs e)
    {
        string profileKey = AppConfigService.Instance.CurrentProfileKey;
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

        // The size has to be known before pouring: it is what the level warnings count down from.
        _pendingChannels = selectedChannels;

        bool anyKeg = selectedChannels.Any(ch => ch > PumpChannels);
        bool anyBottle = selectedChannels.Any(ch => ch <= PumpChannels);
        bool mixed = anyKeg && anyBottle;

        // A single size is applied to every selected line, so the two kinds cannot be mixed.
        MixedSelectionNote.IsVisible = mixed;
        KegSizes.IsVisible = anyKeg && !mixed;
        BottleSizes.IsVisible = anyBottle && !mixed;
        SizeOverlayTitle.Text = anyKeg && !mixed
            ? "Da quanti litri è il fusto che hai inserito?"
            : "Da quanti litri è la bottiglia che hai inserito?";

        SizeOverlay.IsVisible = true;
    }

    private void OnBottleSizeCancel(object? sender, RoutedEventArgs e)
    {
        SizeOverlay.IsVisible = false;
        _pendingChannels = new List<int>();
    }

    private async void OnBottleSizeChosen(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out int millilitres))
            return;

        SizeOverlay.IsVisible = false;
        var channels = _pendingChannels;
        _pendingChannels = new List<int>();
        if (channels.Count == 0) return;

        ApplyBottleSize(channels, millilitres);
        await RunFill(channels);
    }

    /// <summary>Stores the new bottle size on each filled line and restarts its level count.</summary>
    private static void ApplyBottleSize(IReadOnlyList<int> channels, int millilitres)
    {
        var config = AppConfigService.Instance.Config;
        var capacities = config.BottleCapacitiesMl;
        if (capacities.Length < config.LiquidAssignments.Length)
        {
            var grown = new int[config.LiquidAssignments.Length];
            capacities.CopyTo(grown, 0);
            capacities = grown;
            config.BottleCapacitiesMl = grown;
        }

        foreach (int channel in channels)
        {
            int index = channel - 1;
            if (index >= 0 && index < capacities.Length)
                capacities[index] = millilitres;

            MachineWarningService.Instance.ResetLine(channel);
        }

        AppConfigService.Instance.SaveConfig("bottle-size");
        Console.WriteLine($"[ContainerFillPage] Bottle size {millilitres}ml on channels {string.Join(", ", channels)}");
    }

    private async Task RunFill(List<int> selectedChannels)
    {
        string profileKey = AppConfigService.Instance.CurrentProfileKey;
        using var runtimeOperation = MachineRuntimeState.Instance.BeginOperation("fill");

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

        bool sent = manager.TrySendActive(command, out ActivateResponse response);
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
        await ShowFillProgress(delayMs, filledChannels);
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

    /// <summary>Runs the wait as a visible progress bar rather than a frozen screen.</summary>
    private async Task ShowFillProgress(int durationMs, List<int> channels)
    {
        var assignments = AppConfigService.Instance.Config.GetActiveLiquidAssignments();
        var names = channels
            .Select(ch => ch - 1 < assignments.Length && !string.IsNullOrWhiteSpace(assignments[ch - 1])
                ? assignments[ch - 1]
                : $"Line {ch}");
        FillProgressDetail.Text = string.Join("   ·   ", names);

        FillProgress.Value = 0;
        FillProgressOverlay.IsVisible = true;
        try
        {
            const int updateIntervalMs = 50;
            int elapsed = 0;
            while (elapsed < durationMs)
            {
                await Task.Delay(updateIntervalMs);
                elapsed += updateIntervalMs;
                FillProgress.Value = Math.Min(100.0 * elapsed / durationMs, 100);
            }
        }
        finally
        {
            FillProgressOverlay.IsVisible = false;
        }
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
}
