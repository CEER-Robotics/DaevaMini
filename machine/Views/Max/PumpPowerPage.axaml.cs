using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DaevaMini.Config;
using DaevaMini.Services;

namespace DaevaMini.Views.Max;

/// <summary>
/// How hard each bottle pump pushes when pouring a drink. Flat out they spray into the
/// glass, so the bar turns each one down here and checks the result with a short test
/// pour on that line.
/// </summary>
/// <remarks>
/// One value per pump, not one for all: the pumps are the same model but do not run at
/// the same speed, so a figure that stops one spraying leaves another too timid. The kegs
/// are not on this page at all - they are solenoid valves pushed by the keg's pressure,
/// and a valve on a partial duty can chatter or not open.
///
/// Lowering a pump's power lowers what it delivers per second, so every dose on that line
/// would come out short. The page says so as soon as a value moves, and sends the bar to
/// the measuring page rather than pretending the calibration still holds.
/// </remarks>
public partial class PumpPowerPage : UserControl, INotifyPropertyChanged
{
    private const int Step = 5;

    /// <summary>Long enough to see the jet settle, short enough not to fill the glass.</summary>
    private const int TestDurationMs = 5000;

    /// <summary>
    /// Test pour plus the firmware's ENDING animation, before the board is back in WAIT
    /// and would accept another ACTIVE.
    /// </summary>
    private const int TestCooldownMs = TestDurationMs + 1500;

    private bool _changed;
    private bool _testing;

    public PumpPowerPage()
    {
        InitializeComponent();
        LoadLines();
    }

    public ObservableCollection<PumpLine> Lines { get; } = new();

    public bool HasLines => Lines.Count > 0;
    public bool IsEmpty => Lines.Count == 0;

    /// <summary>Shown once any value has moved: the doses no longer match that pump.</summary>
    public bool ShowRecalibrate => _changed;

    private void LoadLines()
    {
        Lines.Clear();

        var config = AppConfigService.Instance.Config;
        var assignments = config.LiquidAssignments;
        for (int index = 0; index < Math.Min(AppConfig.PumpChannelCount, assignments.Length); index++)
        {
            if (string.IsNullOrWhiteSpace(assignments[index]))
                continue;

            int channel = index + 1;
            Lines.Add(new PumpLine(channel, assignments[index], config.GetPumpPowerPercent(channel)));
        }

        OnPropertyChanged(nameof(HasLines));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnDecreaseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: PumpLine line })
            SetPower(line, line.Percent - Step);
    }

    private void OnIncreaseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: PumpLine line })
            SetPower(line, line.Percent + Step);
    }

    private void SetPower(PumpLine line, int percent)
    {
        if (_testing) return;

        percent = Math.Clamp(percent, AppConfig.MinPumpPowerPercent, 100);
        if (percent == line.Percent) return;

        var config = AppConfigService.Instance.Config;
        config.SetPumpPowerPercent(line.Channel, percent);
        AppConfigService.Instance.SaveConfig("pump-power");

        line.Percent = config.GetPumpPowerPercent(line.Channel);
        _changed = true;
        StatusLine.Text = string.Empty;
        OnPropertyChanged(nameof(ShowRecalibrate));
    }

    /// <summary>Locks every row while one is pouring, so nothing changes under the test.</summary>
    private void SetTesting(bool testing)
    {
        _testing = testing;
        foreach (var line in Lines)
            line.Busy = testing;
    }

    /// <summary>A short pour at this line's power, to see whether it still sprays.</summary>
    private async void OnTestClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: PumpLine line } || _testing) return;

        var manager = ArduinoSerialManager.Instance;
        if (!manager.IsConnected)
        {
            StatusLine.Text = "La macchina non risponde: controlla che sia accesa e collegata.";
            return;
        }

        SetTesting(true);
        using var operation = MachineRuntimeState.Instance.BeginOperation("pump-test");
        try
        {
            var durations = new Dictionary<int, int> { [line.Channel] = TestDurationMs };
            string command = ArduinoProtocolHelper.BuildActiveCommand("CYAN", durations,
                AppConfigService.Instance.Config.GetPumpDuty);
            Console.WriteLine($"[PumpPowerPage] Sending: {command}");

            (bool sent, ActivateResponse response) = await manager.TrySendActiveAsync(command);
            if (!sent || response != ActivateResponse.Success)
            {
                StatusLine.Text = $"{line.Liquid}: la macchina non ha accettato il comando. Riprova fra qualche secondo.";
                return;
            }

            StatusLine.Text = $"{line.Liquid}: prova al {line.Percent}% in corso...";
            await Task.Delay(TestCooldownMs);
            StatusLine.Text = $"{line.Liquid}: prova al {line.Percent}% finita.";
        }
        finally
        {
            SetTesting(false);
        }
    }

    private void OnMeasureClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowFlowRatePage();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSettingsPage();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One bottle pump on the page, with its own power.</summary>
public sealed class PumpLine : INotifyPropertyChanged
{
    private int _percent;
    private bool _busy;

    public PumpLine(int channel, string liquid, int percent)
    {
        Channel = channel;
        Liquid = liquid;
        _percent = percent;
    }

    public int Channel { get; }
    public string Liquid { get; }
    public string ChannelLabel => $"Linea {Channel}";

    public int Percent
    {
        get => _percent;
        set
        {
            if (_percent == value) return;
            _percent = value;
            Notify();
        }
    }

    /// <summary>True while any line on the page is test-pouring.</summary>
    public bool Busy
    {
        get => _busy;
        set
        {
            if (_busy == value) return;
            _busy = value;
            Notify();
        }
    }

    public string PowerLabel => $"{_percent}%";

    public bool CanDecrease => !_busy && _percent > AppConfig.MinPumpPowerPercent;
    public bool CanIncrease => !_busy && _percent < 100;
    public bool CanTest => !_busy;

    private void Notify()
    {
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(PowerLabel));
        OnPropertyChanged(nameof(Busy));
        OnPropertyChanged(nameof(CanDecrease));
        OnPropertyChanged(nameof(CanIncrease));
        OnPropertyChanged(nameof(CanTest));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
