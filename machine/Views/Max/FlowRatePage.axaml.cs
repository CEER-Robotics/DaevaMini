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
/// Teaches the machine how fast each keg pours, by pouring for a fixed time and asking
/// how much came out.
/// </summary>
/// <remarks>
/// This used to be a table of millilitres per second with a "-" and a "+": you poured for
/// ten seconds, weighed the glass, divided the grams by ten in your head, and then tapped
/// "+" until the number matched - forty-odd taps for a fast line. Both the arithmetic and
/// the unit were the machine's problem being handed to whoever was behind the bar.
///
/// Now the page asks one question in plain words - how many millilitres did you collect -
/// and does the division itself. The stored value is unchanged (mL/s per channel), so
/// nothing downstream of this page had to move.
/// </remarks>
public partial class FlowRatePage : UserControl, INotifyPropertyChanged
{
    private const int TestSeconds = 10;
    private const int TestDurationMs = TestSeconds * 1000;

    private LineFlowRate? _measuring;
    private bool _testRunning;

    public FlowRatePage()
    {
        InitializeComponent();
        LoadLines();
    }

    public ObservableCollection<LineFlowRate> Lines { get; } = new();

    /// <summary>
    /// Kegs only: the pumps have a fixed throughput the machine already knows, while a
    /// keg's depends on its pressure, so it is the one thing that has to be measured.
    /// </summary>
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

        OnPropertyChanged(nameof(HasLines));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public bool HasLines => Lines.Count > 0;
    public bool IsEmpty => Lines.Count == 0;

    /// <summary>
    /// Measuring a keg that is not connected pours into a closed valve, so the page says
    /// so up front rather than letting someone run ten fruitless tests.
    /// </summary>
    public bool AreKegsOff => !AppConfigService.Instance.Config.IsLargeEvent;

    public bool CanMeasure => !AreKegsOff;

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSettingsPage();
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: LineFlowRate line }) return;

        line.Stored = 0;
        Save(line, $"{line.Liquid}: rimesso il valore standard.");
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

    // ------------------------------------------------------------ the guided measurement

    /// <summary>Where the wizard is: shut, explaining, pouring, or asking for the amount.</summary>
    private enum Step { Closed, Ready, Pouring, Asking, Done }

    private Step _step = Step.Closed;

    public bool IsWizardOpen => _step != Step.Closed;
    public bool IsReady => _step == Step.Ready;
    public bool IsPouring => _step == Step.Pouring;
    public bool IsAsking => _step == Step.Asking;
    public bool IsDone => _step == Step.Done;

    public string WizardTitle => _measuring == null ? string.Empty : _measuring.Liquid;

    private int _secondsLeft;
    public string CountdownLabel => $"{_secondsLeft}";

    /// <summary>Millilitres typed on the pad, as text so leading zeros never appear.</summary>
    private string _collected = string.Empty;

    public string CollectedLabel => _collected.Length == 0 ? "0" : _collected;

    public bool CanConfirm => int.TryParse(_collected, out int ml) && ml > 0;

    public string DoneLabel { get; private set; } = string.Empty;

    private void SetStep(Step step)
    {
        _step = step;
        OnPropertyChanged(nameof(IsWizardOpen));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(IsPouring));
        OnPropertyChanged(nameof(IsAsking));
        OnPropertyChanged(nameof(IsDone));
    }

    private void OnMeasureClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: LineFlowRate line }) return;

        _measuring = line;
        _collected = string.Empty;
        OnPropertyChanged(nameof(WizardTitle));
        OnPropertyChanged(nameof(CollectedLabel));
        OnPropertyChanged(nameof(CanConfirm));
        SetStep(Step.Ready);
    }

    private void OnCancelWizard(object? sender, RoutedEventArgs e)
    {
        if (_testRunning) return;   // never leave a pour running behind a shut door

        _measuring = null;
        SetStep(Step.Closed);
    }

    /// <summary>Pours the line for a fixed time so the amount can be measured by hand.</summary>
    private async void OnStartPourClick(object? sender, RoutedEventArgs e)
    {
        if (_measuring is not { } line || _testRunning) return;

        var manager = ArduinoSerialManager.Instance;
        if (!manager.IsConnected)
        {
            StatusLine.Text = "La macchina non risponde: controlla che sia accesa e collegata.";
            SetStep(Step.Closed);
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
                StatusLine.Text = $"{line.Liquid}: la macchina non ha accettato il comando. Riprova fra qualche secondo.";
                SetStep(Step.Closed);
                return;
            }

            SetStep(Step.Pouring);
            for (_secondsLeft = TestSeconds; _secondsLeft > 0; _secondsLeft--)
            {
                OnPropertyChanged(nameof(CountdownLabel));
                await Task.Delay(1000);
            }

            SetStep(Step.Asking);
        }
        finally
        {
            _testRunning = false;
        }
    }

    private void OnDigitClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string digit }) return;
        if (_collected.Length >= 4) return;                 // 9999 ml is far past any keg
        if (_collected.Length == 0 && digit == "0") return; // no leading zero

        _collected += digit;
        OnPropertyChanged(nameof(CollectedLabel));
        OnPropertyChanged(nameof(CanConfirm));
    }

    private void OnDigitBackspace(object? sender, RoutedEventArgs e)
    {
        if (_collected.Length == 0) return;

        _collected = _collected[..^1];
        OnPropertyChanged(nameof(CollectedLabel));
        OnPropertyChanged(nameof(CanConfirm));
    }

    /// <summary>
    /// Turns the collected amount into the stored rate. This division is the whole point
    /// of the page: it used to be done in the bartender's head.
    /// </summary>
    private void OnConfirmAmountClick(object? sender, RoutedEventArgs e)
    {
        if (_measuring is not { } line) return;
        if (!int.TryParse(_collected, out int ml) || ml <= 0) return;

        double perSecond = Math.Clamp((double)ml / TestSeconds, 1, 500);
        line.Stored = Math.Round(perSecond, 1);

        Save(line, $"{line.Liquid}: velocità aggiornata.");

        DoneLabel = $"{line.Liquid}: {ml} ml in {TestSeconds} secondi. {line.PlainRate}";
        OnPropertyChanged(nameof(DoneLabel));
        SetStep(Step.Done);
    }

    private void OnCloseDone(object? sender, RoutedEventArgs e)
    {
        _measuring = null;
        SetStep(Step.Closed);
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One keg line on the page.</summary>
public sealed class LineFlowRate : INotifyPropertyChanged
{
    /// <summary>A glassful, used to say the rate in something you can picture.</summary>
    private const double ReferenceGlassMl = 200;

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
            OnPropertyChanged(nameof(PlainRate));
            OnPropertyChanged(nameof(SourceLabel));
            OnPropertyChanged(nameof(IsMeasured));
        }
    }

    public double Effective => _stored > 0 ? _stored : Fallback;

    public string ChannelLabel => $"Fusto {Channel}";

    /// <summary>
    /// The rate as a thing you can picture. "45 mL/s" means nothing to most people;
    /// how long it takes to fill a glass does.
    /// </summary>
    public string PlainRate
    {
        get
        {
            double seconds = Effective > 0 ? ReferenceGlassMl / Effective : 0;

            return seconds >= 10
                ? $"Riempie un bicchiere in {seconds:0} secondi"
                : $"Riempie un bicchiere in {seconds:0.#} secondi";
        }
    }

    public bool IsMeasured => _stored > 0;

    /// <summary>
    /// Says where the number comes from without claiming who put it there: a line can
    /// carry its own rate straight from the shipped configuration, never having been
    /// measured by anyone.
    /// </summary>
    public string SourceLabel => _stored > 0
        ? "Velocità impostata per questo fusto"
        : "Non ancora misurata: usa il valore standard";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
