using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DaevaMini.Config;

namespace DaevaMini.Views.Max;

/// <summary>
/// Recipe tuning on the machine: pick a cocktail, nudge each ingredient. Saves straight
/// into the stored configuration, so a drink can be corrected mid-service without
/// opening the Pi.
/// </summary>
public partial class DosesPage : UserControl
{
    private const int StepMl = 5;
    private const int MinMl = 5;
    private const int MaxMl = 500;

    private DoseCocktail? _selected;

    public DosesPage()
    {
        InitializeComponent();
        LoadCocktails();

        if (Cocktails.Count > 0)
            Select(Cocktails[0]);
    }

    public ObservableCollection<DoseCocktail> Cocktails { get; } = new();
    public ObservableCollection<DoseIngredient> Ingredients { get; } = new();

    private void LoadCocktails()
    {
        Cocktails.Clear();
        foreach (var cocktail in AppConfigService.Instance.Config.Cocktails)
            Cocktails.Add(new DoseCocktail(cocktail));
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSettingsPage();
    }

    private void OnSelectCocktail(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: DoseCocktail cocktail }) return;

        Select(cocktail);
    }

    private void Select(DoseCocktail cocktail)
    {
        foreach (var other in Cocktails)
            other.IsSelected = ReferenceEquals(other, cocktail);

        _selected = cocktail;
        SelectedName.Text = cocktail.Name;
        SelectedTotal.Text = cocktail.TotalLabel;

        Ingredients.Clear();
        foreach (var ingredient in cocktail.Config.Ingredients)
            Ingredients.Add(new DoseIngredient(ingredient));
    }

    private void OnIncreaseClick(object? sender, RoutedEventArgs e) => Adjust(sender, +StepMl);

    private void OnDecreaseClick(object? sender, RoutedEventArgs e) => Adjust(sender, -StepMl);

    private void Adjust(object? sender, int delta)
    {
        if (sender is not Button { CommandParameter: DoseIngredient ingredient }) return;
        if (_selected == null) return;

        ingredient.Milliliters = Math.Clamp(ingredient.Milliliters + delta, MinMl, MaxMl);

        AppConfigService.Instance.SaveConfig("doses");

        _selected.RefreshTotal();
        SelectedTotal.Text = _selected.TotalLabel;
        StatusLine.Text = $"{_selected.Name}: {ingredient.Name} {ingredient.Milliliters} ml";
    }
}

/// <summary>A cocktail in the picker on the left.</summary>
public sealed class DoseCocktail : INotifyPropertyChanged
{
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.Parse("#CAF0F8"));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#E7E6DC"));

    private bool _isSelected;

    public DoseCocktail(CocktailConfig config) => Config = config;

    public CocktailConfig Config { get; }
    public string Name => Config.Name;

    public string TotalLabel => $"{Config.Ingredients.Sum(i => i.Milliliters)} ml";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(NameBrush));
        }
    }

    public IBrush NameBrush => _isSelected ? SelectedBrush : IdleBrush;

    public void RefreshTotal() => OnPropertyChanged(nameof(TotalLabel));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One ingredient row, writing straight through to the stored recipe.</summary>
public sealed class DoseIngredient : INotifyPropertyChanged
{
    private readonly IngredientConfig _config;

    public DoseIngredient(IngredientConfig config) => _config = config;

    public string Name => _config.Name;

    public int Milliliters
    {
        get => _config.Milliliters;
        set
        {
            if (_config.Milliliters == value) return;
            _config.Milliliters = value;
            OnPropertyChanged(nameof(Milliliters));
            OnPropertyChanged(nameof(AmountLabel));
        }
    }

    public string AmountLabel => $"{Milliliters} ml";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
