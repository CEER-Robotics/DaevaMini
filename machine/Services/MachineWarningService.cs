using System;
using Avalonia.Media;
using System.Collections.Generic;
using System.Linq;
using DaevaMini.Config;
using DaevaMini.Models;

namespace DaevaMini.Services;

/// <summary>Severity of a warning, used to colour the menu badge.</summary>
public enum WarningLevel
{
    /// <summary>Something needs attention soon, e.g. a bottle running low.</summary>
    Warning,

    /// <summary>Something is already broken, e.g. an empty bottle or a missing line.</summary>
    Critical
}

public sealed record MachineWarning(WarningLevel Level, string Title, string Detail)
{
    public static readonly IBrush CriticalBrush = new SolidColorBrush(Color.Parse("#E5484D"));
    public static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#F5C518"));

    /// <summary>Red for something already broken, amber for something about to break.</summary>
    public IBrush Accent => Level == WarningLevel.Critical ? CriticalBrush : WarningBrush;
}

/// <summary>
/// Collects the consumable problems the machine can work out on its own: bottles running
/// low or empty, and cocktails whose ingredients have no line assigned. Hardware and
/// connectivity faults are not covered here - those belong to the splash status badge.
/// </summary>
public sealed class MachineWarningService
{
    /// <summary>A line is flagged once less than this share of the bottle is left.</summary>
    private const double LowThreshold = 0.15;

    private static MachineWarningService? _instance;
    private static readonly object LockObject = new();

    private readonly LocalMachineStore _store = LocalMachineStore.Instance;
    private IReadOnlyList<MachineWarning> _warnings = Array.Empty<MachineWarning>();

    public static MachineWarningService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (LockObject)
                {
                    _instance ??= new MachineWarningService();
                }
            }
            return _instance;
        }
    }

    private MachineWarningService()
    {
        AppConfigService.Instance.ConfigChanged += (_, _) => Refresh();
    }

    /// <summary>Raised whenever the warning list changes.</summary>
    public event EventHandler? WarningsChanged;

    public IReadOnlyList<MachineWarning> Warnings => _warnings;

    public bool HasWarnings => _warnings.Count > 0;

    /// <summary>Recomputes the warnings from the current config and the stored line levels.</summary>
    public void Refresh()
    {
        var config = AppConfigService.Instance.Config;
        string profileKey = AppConfigService.Instance.CurrentProfileKey;
        var consumption = _store.GetLineConsumption(profileKey);

        var warnings = new List<MachineWarning>();
        warnings.AddRange(BuildLevelWarnings(config, consumption));
        warnings.AddRange(BuildMissingLineWarnings(config));

        _warnings = warnings;
        WarningsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Books what a pour took out of the bottles, then refreshes the warnings.</summary>
    public void RecordConsumption(IReadOnlyDictionary<int, int> millilitresPerChannel)
    {
        _store.AddLineConsumption(AppConfigService.Instance.CurrentProfileKey, millilitresPerChannel);
        Refresh();
    }

    /// <summary>Marks the bottle on the given line as replaced.</summary>
    public void ResetLine(int channel)
    {
        _store.ResetLineConsumption(AppConfigService.Instance.CurrentProfileKey, channel);
        Refresh();
    }

    private static IEnumerable<MachineWarning> BuildLevelWarnings(
        AppConfig config, IReadOnlyDictionary<int, int> consumption)
    {
        var assignments = config.GetActiveLiquidAssignments();
        for (int index = 0; index < assignments.Length; index++)
        {
            string liquid = assignments[index];
            if (string.IsNullOrWhiteSpace(liquid))
                continue;

            // An unknown or zero capacity means the line is deliberately not tracked.
            int capacity = index < config.BottleCapacitiesMl.Length ? config.BottleCapacitiesMl[index] : 0;
            if (capacity <= 0)
                continue;

            int channel = index + 1;
            consumption.TryGetValue(channel, out int used);
            int left = capacity - used;

            if (left <= 0)
            {
                yield return new MachineWarning(
                    WarningLevel.Critical,
                    $"{liquid} esaurita",
                    $"Linea {channel}: sostituire il contenitore.");
            }
            else if (left <= capacity * LowThreshold)
            {
                yield return new MachineWarning(
                    WarningLevel.Warning,
                    $"{liquid} in esaurimento",
                    $"Linea {channel}: restano circa {left} ml su {capacity}.");
            }
        }
    }

    private static IEnumerable<MachineWarning> BuildMissingLineWarnings(AppConfig config)
    {
        var assigned = CocktailAvailability.LoadedLiquids(config);

        foreach (var cocktail in EnumerateCocktails(config))
        {
            if (!cocktail.IsActive)
                continue;

            // Keg-only drinks are off the menu at a small event, so a missing line is expected.
            if (!config.IsLargeEvent && CocktailAvailability.RequiresKegs(cocktail, config))
                continue;

            var missing = CocktailAvailability.MissingLiquids(cocktail, assigned);

            if (missing.Count == 0)
                continue;

            yield return new MachineWarning(
                WarningLevel.Critical,
                $"{cocktail.Name}: manca una linea",
                $"Nessuna pompa assegnata a {string.Join(", ", missing)}. Il drink resta fuori dal menu.");
        }
    }

    /// <summary>Max keeps a flat cocktail list, Mini nests them under modes.</summary>
    private static IEnumerable<CocktailConfig> EnumerateCocktails(AppConfig config)
    {
        foreach (var cocktail in config.Cocktails)
            yield return cocktail;

        foreach (var mode in config.Modes)
            foreach (var cocktail in mode.Cocktails)
                yield return cocktail;
    }
}
