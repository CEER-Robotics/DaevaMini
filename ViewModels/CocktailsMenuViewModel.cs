using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using DaevaMini.Config;

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

public sealed class CocktailsMenuViewModel : INotifyPropertyChanged
{
    private readonly ModesViewModel _modesViewModel;
    private bool _isDispensing = false;
    private ObservableCollection<CocktailVm> _cocktails = new();

    public ObservableCollection<CocktailVm> Cocktails
    {
        get => _cocktails;
        private set
        {
            if (_cocktails == value) return;
            _cocktails = value;
            OnPropertyChanged();
        }
    }

    public bool IsDispensing
    {
        get => _isDispensing;
        set
        {
            if (_isDispensing == value) return;
            _isDispensing = value;
            OnPropertyChanged();
        }
    }

    public CocktailsMenuViewModel(ModesViewModel modesViewModel)
    {
        _modesViewModel = modesViewModel;
        
        // Listen to mode changes to update cocktails
        _modesViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ModesViewModel.CurrentMode))
            {
                LoadCocktailsFromCurrentMode();
            }
        };
        
        // Load initial cocktails
        LoadCocktailsFromCurrentMode();
    }

    private void LoadCocktailsFromCurrentMode()
    {
        var config = AppConfigService.Instance.Config;
        var currentModeName = _modesViewModel.CurrentMode.Name;
        
        // Find the mode config that matches the current mode
        var modeConfig = config.Modes.FirstOrDefault(m => m.Name == currentModeName);
        
        if (modeConfig != null)
        {
            Cocktails = new ObservableCollection<CocktailVm>(
                modeConfig.Cocktails.Select(c => new CocktailVm(
                    c.Name,
                    c.Ingredients.Select(i => new Ingredient(i.Name, i.Milliliters)).ToArray()
                ))
            );
        }
        else
        {
            Cocktails = new ObservableCollection<CocktailVm>();
        }
    }

    /// <summary>
    /// Maps cocktail ingredients to container positions (channels) based on the current mode.
    /// Returns a dictionary mapping channel number (1-4) to duration in milliseconds.
    /// </summary>
    public Dictionary<int, int> MapIngredientsToChannels(CocktailVm cocktail)
    {
        var channelDurations = new Dictionary<int, int>();
        var currentMode = _modesViewModel.CurrentMode;

        // Get flow rate from config
        var config = AppConfigService.Instance.Config;
        int millisecondsPerMilliliter = config.FlowRate.MillisecondsPerMilliliter;

        foreach (var ingredient in cocktail.Ingredients)
        {
            // Find which position (0-3) contains this liquid in the current mode
            for (int position = 0; position < currentMode.LiquidAssignments.Length; position++)
            {
                if (currentMode.LiquidAssignments[position].Equals(ingredient.Name, StringComparison.OrdinalIgnoreCase))
                {
                    // Position maps to channel: position 0 -> c1, position 1 -> c2, etc.
                    int channel = position + 1;
                    int durationMs = ingredient.Milliliters * millisecondsPerMilliliter;

                    // If multiple ingredients use the same channel, sum the durations
                    if (channelDurations.ContainsKey(channel))
                    {
                        channelDurations[channel] += durationMs;
                    }
                    else
                    {
                        channelDurations[channel] = durationMs;
                    }
                    break;
                }
            }
        }

        return channelDurations;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}