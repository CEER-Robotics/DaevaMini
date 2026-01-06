using System.Collections.ObjectModel;

namespace DaevaMini.ViewModels;

public sealed class Ingredient
{
    public string Name { get; }
    public int Milliliters { get; }
    
    public Ingredient(string name, int milliliters)
    {
        Name = name;
        Milliliters = milliliters;
    }
}

public sealed class CocktailVm
{
    public string Name { get; }
    public Ingredient[]  Ingredients { get; }

    public CocktailVm(string name,  Ingredient[] ingredients)
    {
        Name = name;
        Ingredients = ingredients;
    }
}

public sealed class CocktailsMenuViewModel
{
    public ObservableCollection<CocktailVm> Cocktails { get; } = new()
    {
        new(
            "Gin Tonic", 
            [
                new Ingredient("Gin", 50),
                new Ingredient("Tonic", 150),
            ]
        ),
        new(
            "Gin Lemon", 
            [
                new Ingredient("Gin", 50),
                new Ingredient("Lemon", 150),
            ]
        ),
    };
}