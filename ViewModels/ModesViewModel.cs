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
    public string[] LiquidAssignments { get; }

    public Mode(string name, string color, string[] liquidAssignments)
    {
        Name = name;
        Color = Brush.Parse(color);
        LiquidAssignments = liquidAssignments;
    }
    
    public string GetLiquidForPosition(int position)
    {
        if (position >= 0 && position < LiquidAssignments.Length)
            return LiquidAssignments[position];
        return string.Empty;
    }
}

public sealed class ModesViewModel : INotifyPropertyChanged
{
    public static ObservableCollection<Mode> Modes { get; } = new()
    {
        // Position 0: Top-Left, 1: Bottom-Left, 2: Top-Right, 3: Bottom-Right
        new("Gin Mode", "#559CAD", ["Gin", "Tonic", "Lemon", "Red-bull"]),
        new("Vodka Mode", "#F5F749", ["Vodka", "Lemon", "Red-bull", "Lemon"]),
        new("OG Mode", "#F46036", ["Gin", "Vodka", "Tonic", "Lemon"])
    };

    private Mode _currentMode = Modes[0];
    public Mode CurrentMode
    {
        get
        {
            return _currentMode;
        }
        set
        {
            if (_currentMode == value) return;
            _currentMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentModeName));
            OnPropertyChanged(nameof(CurrentModeColor));
            OnPropertyChanged(nameof(Position0Liquid));
            OnPropertyChanged(nameof(Position1Liquid));
            OnPropertyChanged(nameof(Position2Liquid));
            OnPropertyChanged(nameof(Position3Liquid));
            Console.Write("Called set on current mode");
        }
    }

    public string CurrentModeName => CurrentMode.Name;
    public IBrush CurrentModeColor => CurrentMode.Color;
    
    // Helper properties for liquid assignments (for easier XAML binding)
    public string Position0Liquid => CurrentMode.GetLiquidForPosition(0);
    public string Position1Liquid => CurrentMode.GetLiquidForPosition(1);
    public string Position2Liquid => CurrentMode.GetLiquidForPosition(2);
    public string Position3Liquid => CurrentMode.GetLiquidForPosition(3);

    public ModesViewModel()
    {
        CurrentMode = Modes[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}