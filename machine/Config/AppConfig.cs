using System;
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
