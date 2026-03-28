using System;
using System.Linq;
using Avalonia.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DaevaMini.Config;

namespace DaevaMini.ViewModels;

public sealed class Mode
{
    public string Name { get; }
    public IBrush Color { get; }
    public string LedColor { get; }
    public string[] LiquidAssignments { get; }

    public Mode(string name, string color, string ledColor, string[] liquidAssignments)
    {
        Name = name;
        Color = Brush.Parse(color);
        LedColor = ledColor;
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
    private static ObservableCollection<Mode>? _modes;
    
    public static ObservableCollection<Mode> Modes
    {
        get
        {
            if (_modes == null)
            {
                LoadModesFromConfig();
            }
            return _modes ?? new ObservableCollection<Mode>();
        }
    }

    private static void LoadModesFromConfig()
    {
        var config = AppConfigService.Instance.Config;
        if (config.Modes.Length > 0)
        {
            _modes = new ObservableCollection<Mode>(
                config.Modes.Select(m => new Mode(m.Name, m.Color, m.LedColor, m.LiquidAssignments))
            );
        }
        else
        {
            // error out
            throw new InvalidOperationException("No modes found in config");
        }
    }

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
        if (Modes.Count > 0)
        {
            CurrentMode = Modes[0];
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}