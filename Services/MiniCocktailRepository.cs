using System;
using System.Collections.Generic;
using System.Linq;
using DaevaMini.Models;

namespace DaevaMini.Services;

public sealed class MiniCocktailRepository : ICocktailRepository
{
    private const string AssetsBase = "avares://DaevaMini/Assets/";

    private static readonly IReadOnlyList<Cocktail> Cocktails =
    [
        new Cocktail
        {
            Id = "gintonic",
            Title = "Gin Tonic",
            Subtitle = "Gin, tonica",
            ImageSource = AssetsBase + "gintonic.png",
            Theme = CocktailTheme.Teal,
            ModeName = "Gin Mode",
            LedRgb = (255, 255, 255),
            Ingredients =
            [
                new CocktailIngredient("Gin", 50),
                new CocktailIngredient("Tonic", 150)
            ]
        },
        new Cocktail
        {
            Id = "ginlemon",
            Title = "Gin Lemon",
            Subtitle = "Gin, lemon",
            ImageSource = AssetsBase + "ginlemon.png",
            Theme = CocktailTheme.Teal,
            ModeName = "Gin Mode",
            LedRgb = (50, 255, 0),
            Ingredients =
            [
                new CocktailIngredient("Gin", 50),
                new CocktailIngredient("Lemon", 150)
            ]
        },

        // Vodka Mode
        new Cocktail
        {
            Id = "vodkalemon",
            Title = "Vodka Lemon",
            Subtitle = "Vodka, lemon",
            ImageSource = AssetsBase + "vodkalemon.png",
            Theme = CocktailTheme.Orange,
            ModeName = "Vodka Mode",
            LedRgb = (50, 255, 0),
            Ingredients =
            [
                new CocktailIngredient("Vodka", 50),
                new CocktailIngredient("Lemon", 150)
            ]
        },
        new Cocktail
        {
            Id = "vodkaredbull",
            Title = "Vodka Red Bull",
            Subtitle = "Vodka, red bull",
            ImageSource = AssetsBase + "vodkaredbull.png",
            Theme = CocktailTheme.Orange,
            ModeName = "Vodka Mode",
            LedRgb = (30, 255, 0),
            Ingredients =
            [
                new CocktailIngredient("Vodka", 50),
                new CocktailIngredient("Red-bull", 150)
            ]
        },
        new Cocktail
        {
            Id = "vodkatonic",
            Title = "Vodka Tonic",
            Subtitle = "Vodka, tonic",
            ImageSource = AssetsBase + "vodkatonic.png",
            Theme = CocktailTheme.Orange,
            ModeName = "Vodka Mode",
            LedRgb = (255, 255, 255),
            Ingredients =
            [
                new CocktailIngredient("Vodka", 50),
                new CocktailIngredient("Tonic", 150)
            ]
        },

        // OG Mode
        new Cocktail
        {
            Id = "negroni",
            Title = "Negroni",
            Subtitle = "Campari, Vermouth, Gin",
            ImageSource = AssetsBase + "negroni.png",
            Theme = CocktailTheme.Burgundy,
            ModeName = "OG Mode",
            LedRgb = (0, 255, 0),
            Ingredients =
            [
                new CocktailIngredient("Gin", 30),
                new CocktailIngredient("Campari", 30),
                new CocktailIngredient("Vermouth", 30)
            ]
        },
        new Cocktail
        {
            Id = "negronisbagliato",
            Title = "Negroni Sbagliato",
            Subtitle = "Campari, Vermouth, Prosecco",
            ImageSource = AssetsBase + "negronisbagliato.png",
            Theme = CocktailTheme.Burgundy,
            ModeName = "OG Mode",
            LedRgb = (10, 255, 0),
            Ingredients =
            [
                new CocktailIngredient("Campari", 50),
                new CocktailIngredient("Vermouth", 80),
                new CocktailIngredient("Prosecco", 80)
            ]
        },
        new Cocktail
        {
            Id = "americano",
            Title = "Americano",
            Subtitle = "Campari, Vermouth",
            ImageSource = AssetsBase + "americano.png",
            Theme = CocktailTheme.Burgundy,
            ModeName = "OG Mode",
            LedRgb = (0, 255, 0),
            Ingredients =
            [
                new CocktailIngredient("Campari", 30),
                new CocktailIngredient("Vermouth", 30)
            ]
        },
        new Cocktail
        {
            Id = "camparispritz",
            Title = "Campari Spritz",
            Subtitle = "Campari, Prosecco",
            ImageSource = AssetsBase + "camparispritz.png",
            Theme = CocktailTheme.Burgundy,
            ModeName = "OG Mode",
            LedRgb = (10, 255, 0),
            Ingredients = [ new CocktailIngredient("Campari", 50), new CocktailIngredient("Prosecco", 80) ]
        }
    ];

    public IReadOnlyList<Cocktail> GetAll() => Cocktails;

    public IReadOnlyList<Cocktail> GetByMode(string modeName) =>
        Cocktails.Where(c => c.ModeName.Equals(modeName, StringComparison.OrdinalIgnoreCase)).ToList();
}
