using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace DaevaMini.Models;

/// <summary>
/// Ingredient dose for a cocktail (from appsettings).
/// </summary>
public sealed record CocktailIngredient(string Name, int Milliliters);

/// <summary>
/// Display model for a cocktail. Used as DataContext for CocktailCard.
/// </summary>
public class Cocktail
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string ModeName { get; init; } = string.Empty;
    /// <summary>Avares path, e.g. avares://Daeva/Assets/negroni.png</summary>
    public string ImageSource { get; init; } = string.Empty;
    public CocktailTheme Theme { get; init; }

    /// <summary>
    /// Optional LED color sent to Arduino as RGB:r,g,b for the ACTIVE command.
    /// When set, overrides the mode's LedColor preset.
    /// </summary>
    public (byte R, byte G, byte B)? LedRgb { get; init; }

    /// <summary>
    /// Ingredient doses for this cocktail (from appsettings for current mode).
    /// Empty when the cocktail is not defined for the current mode.
    /// </summary>
    public IReadOnlyList<CocktailIngredient> Ingredients { get; init; } = Array.Empty<CocktailIngredient>();

    /// <summary>Brush for the card Border background (from Theme).</summary>
    public IBrush CardBackground => CocktailThemeHelper.GetCardBackground(Theme);

    /// <summary>Style class for the Dale button (e.g. DaleBtnBurgundy).</summary>
    public string DaleBtnClass => CocktailThemeHelper.GetDaleBtnClass(Theme);

    /// <summary>When false the card is greyed out and non-selectable in the menu.</summary>
    public bool IsActive { get; init; } = true;
    public bool IsInactive => !IsActive;

    /// <summary>When true the menu card renders <see cref="ImageSource"/> instead of the plain gradient.</summary>
    public bool ShowImage { get; init; }

    /// <summary>Family used by the Max menu filter bar. Empty means the cocktail only shows under Home.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Short tasting note shown while the guest confirms the pour.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Doses as one line, e.g. "Gin 40ml · Tonic 120ml". Kept compact so three
    /// long ingredient names still fit on a single line of the menu card.</summary>
    public string DoseSummary => string.Join(" · ", Ingredients.Select(i => $"{i.Name} {i.Milliliters}ml"));

    public bool IsBurgundyTheme => Theme == CocktailTheme.Burgundy;
    public bool IsTealTheme => Theme == CocktailTheme.Teal;
    public bool IsOrangeTheme => Theme == CocktailTheme.Orange;
}

public enum CocktailTheme
{
    Burgundy, // Negroni family – orange card, burgundy Dale button
    Teal,     // Gin family – teal card, light blue Dale button
    Orange    // Vodka family – yellow card, orange Dale button
}
