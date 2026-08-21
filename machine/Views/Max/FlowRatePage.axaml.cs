using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DaevaMini.Config;
using DaevaMini.Services;

namespace DaevaMini.Views.Max;

/// <summary>
/// Per-line calibration: run a line for a known time, measure what came out, then
/// nudge its milliseconds-per-millilitre until the poured volume matches.
/// </summary>
public partial class FlowRatePage : UserControl
{
    private const int TestDurationMs = 10000;
    private const int Step = 1;

    private bool _testRunning;

    public FlowRatePage()
    {
        InitializeComponent();
        LoadLines();
    }

    public ObservableCollection<LineFlowRate> Lines { get; } = new();

    private void LoadLines()
    {
        Lines.Clear();

        var config = AppConfigService.Instance.Config;
        var assignments = config.LiquidAssignments;

        for (int index = AppConfig.PumpChannelCount; index < assignments.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(assignments[index]))
                continue;

            int channel = index + 1;
            Lines.Add(new LineFlowRate(channel, assignments[index],
                config.GetFlowRateMlPerSecond(channel),
                1000.0 / config.FlowRate.MillisecondsPerMilliliter));
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSettingsPage();
    }

    private void OnIncreaseClick(object? sender, RoutedEventArgs e) => Adjust(sender, +Step);

    private void OnDecreaseClick(object? sender, RoutedEventArgs e) => Adjust(sender, -Step);

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: LineFlowRate line }) return;

        line.Stored = 0;
        Save(line, "riportata alla portata generale della macchina");
    }

    private void Adjust(object? sender, int delta)
    {
        if (sender is not Button { CommandParameter: LineFlowRate line }) return;

        // Starting from the effective value keeps the first tap from jumping.
        double current = line.Effective;
        line.Stored = Math.Clamp(Math.Round(current) + delta, 1, 500);
        Save(line, $"{line.Liquid}: {line.Stored:0} mL/s");
    }

    private void Save(LineFlowRate line, string message)
    {
        var config = AppConfigService.Instance.Config;

        var rates = config.FlowRatesMlPerSecond;
        if (rates.Length < config.LiquidAssignments.Length)
        {
            var grown = new double[config.LiquidAssignments.Length];
            rates.CopyTo(grown, 0);
            rates = grown;
            config.FlowRatesMlPerSecond = grown;
        }

        int index = line.Channel - 1;
        if (index >= 0 && index < rates.Length)
            rates[index] = line.Stored;

        AppConfigService.Instance.SaveConfig("flow-rate");
        StatusLine.Text = message;
    }

    /// <summary>Runs one line for a fixed time so the volume can be measured by hand.</summary>
    private async void OnTestClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: LineFlowRate line }) return;
        if (_testRunning) return;

        var manager = ArduinoSerialManager.Instance;
        if (!manager.IsConnected)
        {
            StatusLine.Text = "Macchina non connessa: impossibile erogare.";
            return;
        }

        _testRunning = true;
        using var operation = MachineRuntimeState.Instance.BeginOperation("flow-test");
        try
        {
            var durations = new System.Collections.Generic.Dictionary<int, int> { [line.Channel] = TestDurationMs };
            string command = ArduinoProtocolHelper.BuildActiveCommand("CYAN", durations);
            if (!manager.TrySendActive(command, out ActivateResponse response) || response != ActivateResponse.Success)
            {
                StatusLine.Text = $"{line.Liquid}: comando rifiutato dalla macchina ({response}).";
                return;
            }

            StatusLine.Text = $"{line.Liquid}: erogazione per 10 secondi, raccogli tutto il liquido...";
            await Task.Delay(TestDurationMs);
            StatusLine.Text = $"{line.Liquid}: pesa il liquido raccolto, dividi i grammi per 10 e imposta quel numero.";
        }
        finally
        {
            _testRunning = false;
        }
    }
}

/// <summary>One line in the calibration list.</summary>
public sealed class LineFlowRate : INotifyPropertyChanged
{
    private double _stored;

    public LineFlowRate(int channel, string liquid, double stored, double fallback)
    {
        Channel = channel;
        Liquid = liquid;
        Fallback = fallback;
        _stored = stored;
    }

    public int Channel { get; }
    public string Liquid { get; }

    /// <summary>Machine-wide throughput, used whenever this line has none of its own.</summary>
    public double Fallback { get; }

    /// <summary>Millilitres per second measured on this line; zero follows the machine default.</summary>
    public double Stored
    {
        get => _stored;
        set
        {
            if (Math.Abs(_stored - value) < 0.001) return;
            _stored = value;
            OnPropertyChanged(nameof(Stored));
            OnPropertyChanged(nameof(Effective));
            OnPropertyChanged(nameof(RateLabel));
            OnPropertyChanged(nameof(SourceLabel));
        }
    }

    public double Effective => _stored > 0 ? _stored : Fallback;

    public string ChannelLabel => $"Linea {Channel}";
    public string RateLabel => $"{Effective:0} mL/s";
    public string SourceLabel => _stored > 0 ? "misurata" : "valore di macchina";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
