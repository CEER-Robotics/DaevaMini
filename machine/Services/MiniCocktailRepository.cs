using System;
using System.Collections.Generic;
using System.Linq;
using DaevaMini.Config;
using DaevaMini.Models;

namespace DaevaMini.Services;

public sealed class MiniCocktailRepository : ICocktailRepository
{
    private const string AssetsBase = "avares://Daeva/Assets/";
    
    public IReadOnlyList<Cocktail> GetAll()
    {
        var config = AppConfigService.Instance.Config;
        var list = new List<Cocktail>();
        foreach (var mode in config.Modes)
            foreach (var c in mode.Cocktails)
                list.Add(MapCocktail(c, mode.Name));
        return list;
    }

    public IReadOnlyList<Cocktail> GetByMode(string modeName) =>
        GetAll().Where(c => c.ModeName.Equals(modeName, StringComparison.OrdinalIgnoreCase)).ToList();

    private static Cocktail MapCocktail(CocktailConfig c, string modeName) => new()
    {
        Id = c.Id,
        Title = c.Name,
        Subtitle = c.Subtitle,
        ImageSource = AssetsBase + c.Image + ".png",
        Theme = Enum.TryParse<CocktailTheme>(c.Theme, ignoreCase: true, out var theme) ? theme : CocktailTheme.Burgundy,
        ModeName = modeName,
        IsActive = c.IsActive,
        ShowImage = c.ShowImage,
        Category = c.Category,
        Description = c.Description,
        LedRgb = c.LedR.HasValue && c.LedG.HasValue && c.LedB.HasValue
            ? ((byte)Math.Clamp(c.LedR.Value, 0, 255),
               (byte)Math.Clamp(c.LedG.Value, 0, 255),
               (byte)Math.Clamp(c.LedB.Value, 0, 255))
            : null,
        Ingredients = c.Ingredients.Select(i => new CocktailIngredient(i.Name, i.Milliliters)).ToArray()
    };
}
