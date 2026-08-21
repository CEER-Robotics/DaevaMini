using System;
using System.Linq;
using YamlDotNet.Serialization;

namespace DaevaMini.Config;

public sealed class AppConfig
{
    [YamlMember(Alias = "FlowRate")]
    public FlowRateConfig FlowRate { get; set; } = new();

    [YamlMember(Alias = "FillDurationMs")]
    public int FillDurationMs { get; set; } = 5000;

    [YamlMember(Alias = "CleanDurationMs")]
    public int CleanDurationMs { get; set; } = 10000;

    [YamlMember(Alias = "SettingsPin")]
    public string SettingsPin { get; set; } = "1234";

    [YamlMember(Alias = "LiquidAssignments")]
    public string[] LiquidAssignments { get; set; } = Array.Empty<string>();
    /// <summary>
    /// Measured throughput of each line in millilitres per second, same order as
    /// LiquidAssignments. Zero or missing falls back to the machine-wide FlowRate:
    /// the pumps share one figure, while a pressurised keg needs its own.
    /// </summary>
    [YamlMember(Alias = "FlowRatesMlPerSecond")]
    public double[] FlowRatesMlPerSecond { get; set; } = Array.Empty<double>();

    /// <summary>Millilitres per second declared for a channel, or 0 when it has none.</summary>
    public double GetFlowRateMlPerSecond(int channel)
    {
        int index = channel - 1;
        return index >= 0 && index < FlowRatesMlPerSecond.Length ? FlowRatesMlPerSecond[index] : 0;
    }

    /// <summary>
    /// Pump timing for one channel (1-based). A line with a measured throughput is
    /// converted here; every other line keeps the machine-wide calibration.
    /// </summary>
    public int GetFlowRateMsPerMl(int channel)
    {
        double mlPerSecond = GetFlowRateMlPerSecond(channel);
        if (mlPerSecond > 0)
            return Math.Max(1, (int)Math.Round(1000.0 / mlPerSecond));

        return FlowRate.MillisecondsPerMilliliter;
    }

    /// <summary>
    /// "Small" runs on the bottle pumps alone; "Large" brings the pressurised kegs online
    /// and silences the pumps holding a liquid the kegs already supply.
    /// </summary>
    [YamlMember(Alias = "EventMode")]
    public string EventMode { get; set; } = "Small";

    /// <summary>Channels above this index are kegs rather than bottle pumps.</summary>
    [YamlIgnore]
    public const int PumpChannelCount = 10;

    [YamlIgnore]
    public bool IsLargeEvent => EventMode.Trim().Equals("Large", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The lines that may actually pour right now. Every consumer - dispensing, level
    /// warnings, the container page - reads this instead of LiquidAssignments, so the
    /// event mode is honoured in one place.
    /// </summary>
    public string[] GetActiveLiquidAssignments()
    {
        var active = (string[])LiquidAssignments.Clone();

        if (!IsLargeEvent)
        {
            // Small event: the kegs are not connected at all.
            for (int i = PumpChannelCount; i < active.Length; i++)
                active[i] = string.Empty;
            return active;
        }

        // Large event: a keg wins over the pump carrying the same liquid.
        var kegLiquids = active
            .Skip(PumpChannelCount)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < Math.Min(PumpChannelCount, active.Length); i++)
        {
            if (!string.IsNullOrWhiteSpace(active[i]) && kegLiquids.Contains(active[i]))
                active[i] = string.Empty;
        }

        return active;
    }


    /// <summary>
    /// Bottle size in millilitres for each line, same order as LiquidAssignments.
    /// Zero or missing means the line is not tracked and raises no level warnings.
    /// </summary>
    [YamlMember(Alias = "BottleCapacitiesMl")]
    public int[] BottleCapacitiesMl { get; set; } = Array.Empty<int>();

    [YamlMember(Alias = "Modes")]
    public ModeConfig[] Modes { get; set; } = Array.Empty<ModeConfig>();

    /// <summary>Max-only: flat cocktail list (no modes).</summary>
    [YamlMember(Alias = "Cocktails")]
    public CocktailConfig[] Cocktails { get; set; } = Array.Empty<CocktailConfig>();
}

public sealed class FlowRateConfig
{
    [YamlMember(Alias = "MillisecondsPerMilliliter")]
    public int MillisecondsPerMilliliter { get; set; } = 10;
}

public sealed class ModeConfig
{
    [YamlMember(Alias = "Name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "Color")]
    public string Color { get; set; } = "#000000";

    /// <summary>Preset name for ACTIVE BASE/COLOR (e.g. ORANGE, CYAN). Defaults to ORANGE if missing or invalid.</summary>
    [YamlMember(Alias = "LedColor")]
    public string LedColor { get; set; } = "ORANGE";

    [YamlMember(Alias = "LiquidAssignments")]
    public string[] LiquidAssignments { get; set; } = Array.Empty<string>();

    [YamlMember(Alias = "Cocktails")]
    public CocktailConfig[] Cocktails { get; set; } = Array.Empty<CocktailConfig>();
}

public sealed class CocktailConfig
{
    [YamlMember(Alias = "Id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "Name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "Subtitle")]
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>Image filename without extension, e.g. "negroni" → avares://Daeva/Assets/negroni.png</summary>
    [YamlMember(Alias = "Image")]
    public string Image { get; set; } = string.Empty;

    /// <summary>One of: Burgundy, Teal, Orange</summary>
    [YamlMember(Alias = "Theme")]
    public string Theme { get; set; } = "Burgundy";

    [YamlMember(Alias = "IsActive")]
    public bool IsActive { get; set; } = true;

    /// <summary>When true the menu card shows the cocktail artwork instead of the plain gradient.</summary>
    [YamlMember(Alias = "ShowImage")]
    public bool ShowImage { get; set; }

    /// <summary>Family used by the menu filter bar, e.g. "Gin". Empty means the cocktail only shows under Home.</summary>
    [YamlMember(Alias = "Category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>Short tasting note shown on the card while the guest confirms the pour.</summary>
    [YamlMember(Alias = "Description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>When true the drink is only on the menu while the kegs are connected.</summary>
    [YamlMember(Alias = "LargeEventOnly")]
    public bool LargeEventOnly { get; set; }

    [YamlMember(Alias = "LedR")]
    public int? LedR { get; set; }

    [YamlMember(Alias = "LedG")]
    public int? LedG { get; set; }

    [YamlMember(Alias = "LedB")]
    public int? LedB { get; set; }

    [YamlMember(Alias = "Ingredients")]
    public IngredientConfig[] Ingredients { get; set; } = Array.Empty<IngredientConfig>();
}

public sealed class IngredientConfig
{
    [YamlMember(Alias = "Name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "Milliliters")]
    public int Milliliters { get; set; }
}
