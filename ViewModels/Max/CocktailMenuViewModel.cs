using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DaevaMini.Models;
using DaevaMini.Services;

namespace DaevaMini.ViewModels.Max;

/// <summary>
/// ViewModel for the Max cocktail menu. Loads all cocktails from the repository
/// without mode filtering — the Max has no concept of modes.
/// </summary>
public sealed class MaxCocktailMenuViewModel : INotifyPropertyChanged
{
    private readonly ICocktailRepository _repository;
    private ObservableCollection<Cocktail> _cocktails = new();

    public MaxCocktailMenuViewModel(ICocktailRepository repository)
    {
        _repository = repository;
        Cocktails = new ObservableCollection<Cocktail>(_repository.GetAll());
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
