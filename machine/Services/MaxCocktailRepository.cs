using System;
using System.Collections.Generic;
using System.Linq;
using DaevaMini.Config;
using DaevaMini.Models;

namespace DaevaMini.Services;

public sealed class MaxCocktailRepository : ICocktailRepository
{
    private const string AssetsBase = "avares://Daeva/Assets/";
    
    public IReadOnlyList<Cocktail> GetAll()
    {
        var config = AppConfigService.Instance.Config;
        var loaded = CocktailAvailability.LoadedLiquids(config);

        return config.Cocktails
            // Switched off in the doses settings: off the menu entirely, not shown
            // greyed out. A drink nobody can order has no business taking a card.
            .Where(c => c.IsActive)
            // Keg drinks are off the menu while the kegs are unplugged. Asked of the
            // current assignments rather than of CocktailConfig.LargeEventOnly, which is
            // a snapshot from save time and goes stale the moment a line is reassigned.
            .Where(c => config.IsLargeEvent || !CocktailAvailability.RequiresKegs(c, config))
            // And nothing whose ingredients are not loaded on some line: the menu
            // follows whatever the containers actually hold.
            .Where(c => CocktailAvailability.CanBeMade(c, loaded))
            .Select(MapCocktail)
            .ToList();
    }

    public IReadOnlyList<Cocktail> GetByMode(string modeName) =>
        GetAll().Where(c => c.ModeName.Equals(modeName, StringComparison.OrdinalIgnoreCase)).ToList();

    private static Cocktail MapCocktail(CocktailConfig c) => new()
    {
        Id = c.Id,
        Title = c.Name,
        Subtitle = c.Subtitle,
        ImageSource = AssetsBase + c.Image + ".png",
        Theme = Enum.TryParse<CocktailTheme>(c.Theme, ignoreCase: true, out var theme) ? theme : CocktailTheme.Burgundy,
        ModeName = string.Empty,
        IsActive = c.IsActive,
        ShowImage = c.ShowImage,
        Category = c.Category,
        Description = c.Description,
        IsTap = c.IsTap,
        LedRgb = c.LedR.HasValue && c.LedG.HasValue && c.LedB.HasValue
            ? ((byte)Math.Clamp(c.LedR.Value, 0, 255),
               (byte)Math.Clamp(c.LedG.Value, 0, 255),
               (byte)Math.Clamp(c.LedB.Value, 0, 255))
            : null,
        Ingredients = c.Ingredients.Select(i => new CocktailIngredient(i.Name, i.Milliliters)).ToArray()
    };
}
