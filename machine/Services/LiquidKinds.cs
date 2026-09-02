using System;
using System.Collections.Generic;
using DaevaMini.Config;

namespace DaevaMini.Services;

/// <summary>
/// Splits the liquids a line can carry into alcoholic bases and mixers.
/// </summary>
/// <remarks>
/// The machine has never recorded this: a line holds a name and nothing else, and the
/// recipes in the configuration only list millilitres. Rather than add a field to every
/// assignment - which would need filling in by hand on machines already in service -
/// the kind is derived from the name, which is the same string the bar staff type.
///
/// A name nobody has classified counts as a mixer. That is the safe way round for the
/// craft page: an unknown liquid still shows up and can still go into a drink, it just
/// does not get offered as the alcoholic base of one.
/// </remarks>
public static class LiquidKinds
{
    private static readonly HashSet<string> Alcoholic = new(StringComparer.OrdinalIgnoreCase)
    {
        // Spirits
        "Gin", "Vodka", "Rum", "Tequila", "Whisky", "Whiskey", "Bourbon", "Brandy", "Cognac",
        "Grappa", "Sambuca", "Mezcal",
        // Wines and beer
        "Prosecco", "Spumante", "Vino", "Vino bianco", "Vino rosso", "Birra", "Beer",
        // Vermouth, bitters and liqueurs
        "Vermouth", "Vermut", "Bitter rosso", "Bitter arancione", "Campari", "Aperol",
        "Amaro", "Limoncello", "Triple sec", "Cointreau", "Martini",
    };

    /// <summary>
    /// Listed so the page can say "analcolico" with some confidence instead of falling
    /// back to the default. Nothing breaks when a name is missing here.
    /// </summary>
    private static readonly HashSet<string> NonAlcoholic = new(StringComparer.OrdinalIgnoreCase)
    {
        "Tonica", "Tonic", "Cola", "Coke", "Limone", "Lemon", "Energy", "Acqua", "Water",
        "Soda", "Ginger", "Ginger beer", "Arancia", "Ananas", "Pompelmo", "Succo",
        "Sciroppo", "Menta", "Lime",
    };

    public static bool IsAlcoholic(string? liquid)
        => !string.IsNullOrWhiteSpace(liquid) && Alcoholic.Contains(liquid.Trim());

    /// <summary>
    /// As above, but a liquid the bar added on the machine is whatever they filed it as.
    /// Their answer wins over the built-in list: they are holding the bottle.
    /// </summary>
    public static bool IsAlcoholic(string? liquid, AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(liquid)) return false;

        string name = liquid.Trim();
        foreach (var custom in config.CustomLiquids)
            if (name.Equals(custom.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
                return custom.Alcoholic;

        return Alcoholic.Contains(name);
    }

    /// <summary>
    /// True only for names actually on the mixer list. An unclassified name is treated
    /// as a mixer everywhere else, but this stays false so callers can tell the two apart.
    /// </summary>
    public static bool IsKnownNonAlcoholic(string? liquid)
        => !string.IsNullOrWhiteSpace(liquid) && NonAlcoholic.Contains(liquid.Trim());

    /// <summary>Label for the UI: what this liquid counts as when building a drink.</summary>
    public static string KindLabel(string? liquid)
        => IsAlcoholic(liquid) ? "alcolico" : "analcolico";
}
