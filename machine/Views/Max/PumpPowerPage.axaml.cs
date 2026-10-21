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
/// How hard the bottle pumps push when pouring a drink. Flat out they spray into the
/// glass, so the bar turns them down here and checks the result with a short test pour
/// on any line.
/// </summary>
/// <remarks>
/// One value for all ten pumps: they are the same model and spray the same way. The kegs
/// are not on this page at all - they are solenoid valves pushed by the keg's pressure,
/// and a valve on a partial duty can chatter or not open.
///
/// Lowering the power lowers what a pump delivers per second, so every dose would come
/// out short. The page says so as soon as the value moves, and sends the bar to the
/// measuring page rather than pretending the calibration still holds.
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

    private void LoadLines()
    {
        Lines.Clear();

        var assignments = AppConfigService.Instance.Config.LiquidAssignments;
        for (int index = 0; index < Math.Min(AppConfig.PumpChannelCount, assignments.Length); index++)
        {
            if (!string.IsNullOrWhiteSpace(assignments[index]))
                Lines.Add(new PumpLine(index + 1, assignments[index]));
        }

        OnPropertyChanged(nameof(HasLines));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static int Power => Math.Clamp(
        AppConfigService.Instance.Config.PumpPowerPercent, AppConfig.MinPumpPowerPercent, 100);

    public string PowerLabel => $"{Power}%";

    public string PowerDetail => Power >= 100
        ? "Massima: le pompe vanno alla velocità piena"
        : "Più bassa: getto più morbido, ma versano più lentamente";

    public bool CanDecrease => Power > AppConfig.MinPumpPowerPercent && !_testing;
    public bool CanIncrease => Power < 100 && !_testing;
    public bool CanTest => !_testing;

    /// <summary>Shown once the value has moved: the doses no longer match the pumps.</summary>
    public bool ShowRecalibrate => _changed;

    private void OnDecreaseClick(object? sender, RoutedEventArgs e) => SetPower(Power - Step);

    private void OnIncreaseClick(object? sender, RoutedEventArgs e) => SetPower(Power + Step);

    private void SetPower(int percent)
    {
        percent = Math.Clamp(percent, AppConfig.MinPumpPowerPercent, 100);
        if (percent == Power) return;

        AppConfigService.Instance.Config.PumpPowerPercent = percent;
        AppConfigService.Instance.SaveConfig("pump-power");

        _changed = true;
        StatusLine.Text = string.Empty;
        NotifyPower();
    }

    private void NotifyPower()
    {
        OnPropertyChanged(nameof(PowerLabel));
        OnPropertyChanged(nameof(PowerDetail));
        OnPropertyChanged(nameof(CanDecrease));
        OnPropertyChanged(nameof(CanIncrease));
        OnPropertyChanged(nameof(CanTest));
        OnPropertyChanged(nameof(ShowRecalibrate));
    }

    /// <summary>A short pour at the current power, to see whether it still sprays.</summary>
    private async void OnTestClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: PumpLine line } || _testing) return;

        var manager = ArduinoSerialManager.Instance;
        if (!manager.IsConnected)
        {
            StatusLine.Text = "La macchina non risponde: controlla che sia accesa e collegata.";
            return;
        }

        _testing = true;
        NotifyPower();
        using var operation = MachineRuntimeState.Instance.BeginOperation("pump-test");
        try
        {
            var durations = new Dictionary<int, int> { [line.Channel] = TestDurationMs };
            string command = ArduinoProtocolHelper.BuildActiveCommand("CYAN", durations,
                AppConfigService.Instance.Config.PumpDuty);
            Console.WriteLine($"[PumpPowerPage] Sending: {command}");

            (bool sent, ActivateResponse response) = await manager.TrySendActiveAsync(command);
            if (!sent || response != ActivateResponse.Success)
            {
                StatusLine.Text = $"{line.Liquid}: la macchina non ha accettato il comando. Riprova fra qualche secondo.";
                return;
            }

            StatusLine.Text = $"{line.Liquid}: prova al {Power}% in corso...";
            await Task.Delay(TestCooldownMs);
            StatusLine.Text = $"{line.Liquid}: prova al {Power}% finita.";
        }
        finally
        {
            _testing = false;
            NotifyPower();
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

/// <summary>One bottle pump on the page.</summary>
public sealed class PumpLine
{
    public PumpLine(int channel, string liquid)
    {
        Channel = channel;
        Liquid = liquid;
    }

    public int Channel { get; }
    public string Liquid { get; }
    public string ChannelLabel => $"Linea {Channel}";
}
