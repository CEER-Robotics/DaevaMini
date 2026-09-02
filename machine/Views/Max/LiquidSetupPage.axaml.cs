using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using DaevaMini.Config;
using DaevaMini.Services;

namespace DaevaMini.Views.Max;

/// <summary>
/// Tells the machine what is loaded where. Same plan of the machine as the refill page,
/// but nothing here runs a pump: this page only records which liquid sits on which line.
/// </summary>
/// <remarks>
/// It is deliberately separate from "cambio contenitori". That page is a physical
/// routine - select containers, run the pumps - and mixing "what is loaded" into it put
/// a configuration decision one stray tap away from moving liquid.
/// </remarks>
public partial class LiquidSetupPage : UserControl, INotifyPropertyChanged
{
    /// <summary>
    /// What a keg can hold. The pressurised lines are plumbed for these and nothing
    /// else, so offering the full liquid list there would invite a setup the machine
    /// cannot actually pour. Matched case-insensitively, and both the Italian and
    /// English spellings are accepted because recipes have used both.
    /// </summary>
    private static readonly HashSet<string> KegLiquids = new(StringComparer.OrdinalIgnoreCase)
    {
        "Prosecco", "Cola", "Coke", "Tonic", "Tonica", "Birra", "Beer"
    };

    private readonly ToggleButton?[] _containers;

    /// <summary>Line being reassigned, 1-based. Zero means the picker is shut.</summary>
    private int _editingChannel;

    public LiquidSetupPage()
    {
        InitializeComponent();

        // Index is channel - 1: ten bottle pumps, then the four kegs on P11-P14.
        _containers =
        [
            Bottle1, Bottle2, Bottle3, Bottle4, Bottle5,
            Bottle6, Bottle7, Bottle8, Bottle9, Bottle10,
            Keg1, Keg2, Keg3, Keg4
        ];

        ApplyLabels();
        ApplyKegAvailability();
        RefreshSummary();
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

    private void OnConfigChanged(object? sender, AppConfigChangedEventArgs e)
        => Dispatcher.UIThread.Post(() =>
        {
            ApplyLabels();
            ApplyKegAvailability();
            RefreshSummary();
        });

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSettingsPage();
    }

    /// <summary>
    /// Labels every container with what it is set to hold. Reads the raw assignments,
    /// not the active ones: at a large event a bottle superseded by a keg reads as empty
    /// elsewhere, and here the whole point is to show what is actually recorded.
    /// </summary>
    private void ApplyLabels()
    {
        var assignments = AppConfigService.Instance.Config.LiquidAssignments;

        bool kegsOn = AppConfigService.Instance.Config.IsLargeEvent;

        for (int i = 0; i < _containers.Length; i++)
        {
            if (_containers[i] == null) continue;

            // A blocked keg carries no caption: the red overlay's own message takes that
            // spot, and leaving "— vuoto —" there would print two labels on top of each other.
            if (i >= AppConfig.PumpChannelCount && !kegsOn)
            {
                _containers[i]!.Tag = string.Empty;
                continue;
            }

            bool hasLiquid = i < assignments.Length && !string.IsNullOrWhiteSpace(assignments[i]);
            _containers[i]!.Tag = hasLiquid ? assignments[i] : "— vuoto —";
        }
    }

    /// <summary>
    /// Crosses the kegs out when "Attiva fusti" is off, matching the refill page, and
    /// takes their pencils out of service so a line that cannot pour cannot be reassigned.
    /// </summary>
    private void ApplyKegAvailability()
    {
        bool kegsOn = AppConfigService.Instance.Config.IsLargeEvent;

        Keg1Blocked.IsVisible = !kegsOn;
        Keg2Blocked.IsVisible = !kegsOn;
        Keg3Blocked.IsVisible = !kegsOn;
        Keg4Blocked.IsVisible = !kegsOn;

        KegEdit1.IsEnabled = kegsOn;
        KegEdit2.IsEnabled = kegsOn;
        KegEdit3.IsEnabled = kegsOn;
        KegEdit4.IsEnabled = kegsOn;

        for (int i = AppConfig.PumpChannelCount; i < _containers.Length; i++)
            if (_containers[i] != null)
                _containers[i]!.IsEnabled = kegsOn;
    }

    // ------------------------------------------------------------ what is missing

    public ObservableCollection<MissingLiquid> MissingLiquids { get; } = new();

    public bool IsComplete => MissingLiquids.Count == 0;

    public string SummaryHeadline => IsComplete
        ? "TUTTO PRONTO"
        : MissingLiquids.Count == 1 ? "1 LIQUIDO DA ASSEGNARE" : $"{MissingLiquids.Count} LIQUIDI DA ASSEGNARE";

    public string SummaryDetail => IsComplete
        ? "Ogni cocktail attivo ha i suoi liquidi su una linea."
        : "Finche' mancano, i cocktail che li usano restano fuori dal menu.";

    public IBrush SummaryAccent => IsComplete ? OkAccent : WarnAccent;
    public IBrush SummaryBackground => IsComplete ? OkBackground : WarnBackground;
    public IBrush SummaryBorder => IsComplete ? OkBorder : WarnBorder;

    private static readonly IBrush OkAccent = new SolidColorBrush(Color.Parse("#7FE0A8"));
    private static readonly IBrush OkBackground = new SolidColorBrush(Color.Parse("#0C1610"));
    private static readonly IBrush OkBorder = new SolidColorBrush(Color.Parse("#2C5B41"));
    private static readonly IBrush WarnAccent = new SolidColorBrush(Color.Parse("#E8C46A"));
    private static readonly IBrush WarnBackground = new SolidColorBrush(Color.Parse("#141008"));
    private static readonly IBrush WarnBorder = new SolidColorBrush(Color.Parse("#5A4A16"));

    /// <summary>
    /// Lists liquids rather than drinks: the question this page answers is "what do I
    /// still have to load", so one missing bottle is one line here even when it takes
    /// four cocktails down with it. Each entry names those cocktails, so it is clear
    /// what loading it would buy back.
    /// </summary>
    private void RefreshSummary()
    {
        var config = AppConfigService.Instance.Config;
        var loaded = CocktailAvailability.LoadedLiquids(config);

        var wanted = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var cocktail in config.Cocktails)
        {
            // Only drinks that would otherwise be on the menu: something switched off,
            // or keg-only at a small event, is absent on purpose.
            if (!cocktail.IsActive) continue;
            if (cocktail.LargeEventOnly && !config.IsLargeEvent) continue;

            foreach (var missing in CocktailAvailability.MissingLiquids(cocktail, loaded))
            {
                if (!wanted.TryGetValue(missing, out var users))
                    wanted[missing] = users = new List<string>();
                users.Add(cocktail.Name);
            }
        }

        MissingLiquids.Clear();
        foreach (var entry in wanted.OrderBy(e => e.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            MissingLiquids.Add(new MissingLiquid(
                entry.Key,
                $"serve per: {string.Join(", ", entry.Value)}"));
        }

        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(SummaryHeadline));
        OnPropertyChanged(nameof(SummaryDetail));
        OnPropertyChanged(nameof(SummaryAccent));
        OnPropertyChanged(nameof(SummaryBackground));
        OnPropertyChanged(nameof(SummaryBorder));
    }

    // ----------------------------------------------------------- liquid picker

    public ObservableCollection<string> Liquids { get; } = new();

    public bool IsPickerOpen => _editingChannel > 0;

    private bool EditingKeg => _editingChannel > AppConfig.PumpChannelCount;

    public string PickerTitle => _editingChannel <= 0
        ? string.Empty
        : EditingKeg ? $"FUSTO {_editingChannel}" : $"BOTTIGLIA {_editingChannel}";

    public string PickerSubtitle
    {
        get
        {
            if (_editingChannel <= 0) return string.Empty;

            var assignments = AppConfigService.Instance.Config.LiquidAssignments;
            int index = _editingChannel - 1;
            string current = index < assignments.Length ? assignments[index] : string.Empty;

            string state = string.IsNullOrWhiteSpace(current)
                ? "Linea vuota"
                : $"Ora contiene: {current}";

            return EditingKeg ? $"{state}  ·  i fusti accettano solo prosecco, cola, tonica o birra" : state;
        }
    }

    private void OnEditLine(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (!int.TryParse(button.Tag?.ToString(), out int channel)) return;

        _editingChannel = channel;

        var known = CocktailAvailability.KnownLiquids(AppConfigService.Instance.Config);
        if (EditingKeg)
            known = known.Where(KegLiquids.Contains).ToList();

        Liquids.Clear();
        foreach (var liquid in known)
            Liquids.Add(liquid);

        NotifyPicker();
    }

    private void OnPickLiquid(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string liquid }) return;
        AssignAndClose(liquid);
    }

    private void OnClearLine(object? sender, RoutedEventArgs e) => AssignAndClose(string.Empty);

    private void OnClosePicker(object? sender, RoutedEventArgs e)
    {
        _editingChannel = 0;
        NotifyPicker();
    }

    /// <summary>
    /// Writes the choice into the stored configuration. Saving raises ConfigChanged,
    /// which relabels this page and rebuilds the cocktail menu, so a drink whose liquid
    /// has just arrived shows up without restarting anything.
    /// </summary>
    private void AssignAndClose(string liquid)
    {
        if (_editingChannel <= 0) return;

        var config = AppConfigService.Instance.Config;
        int index = _editingChannel - 1;

        // The stored array can be shorter than the machine when the configuration
        // predates a line being added, so grow it rather than throwing.
        if (config.LiquidAssignments.Length <= index)
        {
            var grown = new string[index + 1];
            for (int i = 0; i < grown.Length; i++)
                grown[i] = i < config.LiquidAssignments.Length ? config.LiquidAssignments[i] : string.Empty;
            config.LiquidAssignments = grown;
        }

        config.LiquidAssignments[index] = liquid;
        AppConfigService.Instance.SaveConfig("liquids");

        _editingChannel = 0;
        NotifyPicker();
        ApplyLabels();
        RefreshSummary();
    }

    private void NotifyPicker()
    {
        OnPropertyChanged(nameof(IsPickerOpen));
        OnPropertyChanged(nameof(PickerTitle));
        OnPropertyChanged(nameof(PickerSubtitle));
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// A liquid no line is carrying, and the drinks waiting for it. Declared at namespace
/// level because compiled bindings need a type XAML can name.
/// </summary>
public sealed record MissingLiquid(string Name, string Detail);
