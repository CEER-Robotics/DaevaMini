using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DaevaMini.Models;
using DaevaMini.Services;

namespace DaevaMini.ViewModels;

/// <summary>
/// ViewModel for the new CocktailMenu (ImprovedInterface style). Uses repository for display list.
/// </summary>
public sealed class CocktailMenuViewModel : INotifyPropertyChanged
{
    private readonly ICocktailRepository _repository;
    private readonly ModesViewModel _modesViewModel;
    private ObservableCollection<Cocktail> _cocktails = new();

    public CocktailMenuViewModel(ICocktailRepository repository, ModesViewModel modesViewModel)
    {
        _repository = repository;
        _modesViewModel = modesViewModel;
        RefreshCocktails();
        _modesViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ModesViewModel.CurrentMode))
                RefreshCocktails();
        };
    }

    public ObservableCollection<Cocktail> Cocktails
    {
        get => _cocktails;
        private set
        {
            if (_cocktails == value) return;
            _cocktails = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Set by the host (e.g. MainWindow) to handle cocktail selection.</summary>
    public ICommand? SelectCocktailCommand { get; set; }

    private void RefreshCocktails()
    {
        var list = _repository.GetAll();
        Cocktails = new ObservableCollection<Cocktail>(list);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
