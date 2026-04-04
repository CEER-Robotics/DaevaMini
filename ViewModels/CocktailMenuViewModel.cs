using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DaevaMini.Models;
using DaevaMini.Services;

namespace DaevaMini.ViewModels;

/// <summary>
/// ViewModel for cocktail menu screens. When modesViewModel is provided, filters by current mode
/// and refreshes on mode change (Mini). When null, loads all cocktails (Max).
/// </summary>
public sealed class CocktailMenuViewModel : INotifyPropertyChanged
{
    private readonly ICocktailRepository _repository;
    private readonly ModesViewModel? _modesViewModel;
    private ObservableCollection<Cocktail> _cocktails = new();

    public CocktailMenuViewModel(ICocktailRepository repository, ModesViewModel? modesViewModel = null)
    {
        _repository = repository;
        _modesViewModel = modesViewModel;
        RefreshCocktails();

        if (_modesViewModel != null)
        {
            _modesViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ModesViewModel.CurrentMode))
                    RefreshCocktails();
            };
        }
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

    /// <summary>Set by the host window to handle cocktail selection.</summary>
    public ICommand? SelectCocktailCommand { get; set; }

    private void RefreshCocktails()
    {
        var list = _modesViewModel?.CurrentMode != null
            ? _repository.GetByMode(_modesViewModel.CurrentMode.Name)
            : _repository.GetAll();
        Cocktails = new ObservableCollection<Cocktail>(list);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
