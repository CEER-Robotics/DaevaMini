using System;
using System.Collections.Generic;
using System.Linq;
using DaevaMini.Config;

namespace DaevaMini.Services;

/// <summary>
/// Whether a recipe can actually be poured with the liquids currently loaded.
/// </summary>
/// <remarks>
/// This used to live inside the warning service, which raised "manca una linea" while
/// the menu happily kept offering the drink. Now the menu hides what it cannot make and
/// the warning explains why to whoever is running the machine, and both ask the same
/// question here so they cannot disagree.
/// </remarks>
public static class CocktailAvailability
{
    /// <summary>
    /// The liquids reachable right now. Reads the *active* assignments, so at a small
    /// event the kegs count as absent and at a large one a keg supersedes the pump
    /// carrying the same liquid.
    /// </summary>
    public static HashSet<string> LoadedLiquids(AppConfig config)
        => config.GetActiveLiquidAssignments()
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ingredients of this recipe that no line is carrying.</summary>
    public static IReadOnlyList<string> MissingLiquids(CocktailConfig cocktail, HashSet<string> loaded)
        => cocktail.Ingredients
            .Where(i => !loaded.Contains(i.Name))
            .Select(i => i.Name)
            .ToList();

    public static bool CanBeMade(CocktailConfig cocktail, HashSet<string> loaded)
        => cocktail.Ingredients.All(i => loaded.Contains(i.Name));

    /// <summary>
    /// Every liquid the machine knows how to use: what the recipes ask for, plus
    /// whatever is already loaded even if no recipe uses it. Sorted, because this is
    /// what the container page offers when reassigning a line.
    /// </summary>
    public static IReadOnlyList<string> KnownLiquids(AppConfig config)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cocktail in config.Cocktails)
            foreach (var ingredient in cocktail.Ingredients)
                if (!string.IsNullOrWhiteSpace(ingredient.Name))
                    names.Add(ingredient.Name.Trim());

        foreach (var mode in config.Modes)
            foreach (var cocktail in mode.Cocktails)
                foreach (var ingredient in cocktail.Ingredients)
                    if (!string.IsNullOrWhiteSpace(ingredient.Name))
                        names.Add(ingredient.Name.Trim());

        foreach (var assigned in config.LiquidAssignments)
            if (!string.IsNullOrWhiteSpace(assigned))
                names.Add(assigned.Trim());

        return names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
