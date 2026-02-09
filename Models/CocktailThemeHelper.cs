using Avalonia.Media;

namespace DaevaMini.Models;

/// <summary>
/// Maps <see cref="CocktailTheme"/> to brush and style class for the card view.
/// </summary>
public static class CocktailThemeHelper
{
    private static readonly SolidColorBrush BurgundyCardBrush = new(Color.Parse("#F46036"));
    private static readonly SolidColorBrush TealCardBrush = new(Color.Parse("#5697A2"));
    private static readonly SolidColorBrush OrangeCardBrush = new(Color.Parse("#FDFD96"));

    public static IBrush GetCardBackground(CocktailTheme theme) => theme switch
    {
        CocktailTheme.Burgundy => BurgundyCardBrush,
        CocktailTheme.Teal => TealCardBrush,
        CocktailTheme.Orange => OrangeCardBrush,
        _ => BurgundyCardBrush
    };

    /// <summary>Style class for the Dale button, e.g. DaleBtnBurgundy.</summary>
    public static string GetDaleBtnClass(CocktailTheme theme) => theme switch
    {
        CocktailTheme.Burgundy => "DaleBtnBurgundy",
        CocktailTheme.Teal => "DaleBtnTeal",
        CocktailTheme.Orange => "DaleBtnOrange",
        _ => "DaleBtnBurgundy"
    };
}
