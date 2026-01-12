using System;
using YamlDotNet.Serialization;

namespace DaevaMini.Config;

public sealed class AppConfig
{
    [YamlMember(Alias = "FlowRate")]
    public FlowRateConfig FlowRate { get; set; } = new();

    [YamlMember(Alias = "Modes")]
    public ModeConfig[] Modes { get; set; } = Array.Empty<ModeConfig>();
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

    [YamlMember(Alias = "LiquidAssignments")]
    public string[] LiquidAssignments { get; set; } = Array.Empty<string>();

    [YamlMember(Alias = "Cocktails")]
    public CocktailConfig[] Cocktails { get; set; } = Array.Empty<CocktailConfig>();
}

public sealed class CocktailConfig
{
    [YamlMember(Alias = "Name")]
    public string Name { get; set; } = string.Empty;

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
