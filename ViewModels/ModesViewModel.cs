using System;
using Avalonia.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DaevaMini.ViewModels;

public sealed class Mode
{
    public string Name { get; }
    public IBrush Color { get; }

    public Mode(string name, string color)
    {
        Name = name;
        Color = Brush.Parse(color);
    }
}

public sealed class ModesViewModel : INotifyPropertyChanged
{
    public static ObservableCollection<Mode> Modes { get; } = new()
    {
        new("Gin Mode", "#559CAD"),
        new("Vodka Mode", "#F5F749"),
        new("OG Mode", "#F46036")
    };

    private Mode _currentMode = Modes[0];
    public Mode CurrentMode
    {
        get
        {
            Console.Write("Called get on current mode");
            return _currentMode;
        }
        set
        {
            if (_currentMode == value) return;
            _currentMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentModeName));
            OnPropertyChanged(nameof(CurrentModeColor));
            Console.Write("Called set on current mode");
        }
    }

    public string CurrentModeName => CurrentMode.Name;
    public IBrush CurrentModeColor => CurrentMode.Color;

    public ModesViewModel()
    {
        CurrentMode = Modes[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}