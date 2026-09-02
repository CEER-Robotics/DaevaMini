using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DaevaMini.Config;

namespace DaevaMini.Views.Max;

/// <summary>
/// Changes the PIN that guards the settings.
/// </summary>
/// <remarks>
/// The current PIN is not asked for again: you are reading this screen, which means you
/// got through the unlock page a moment ago. What the page does guard against is the
/// failure that actually matters here - a typo. A mistyped PIN locks the bar out of its
/// own settings with no way back except editing the machine's database by hand, so the
/// new one has to be entered twice and is only saved when the two agree.
/// </remarks>
public partial class ChangePinPage : UserControl, INotifyPropertyChanged
{
    /// <summary>Four digits, because that is what the unlock screen is built to accept.</summary>
    private const int PinLength = 4;

    private string _first = string.Empty;
    private string _second = string.Empty;

    public ChangePinPage()
    {
        InitializeComponent();
        UpdateDots();
    }

    /// <summary>Which of the two entries is being typed.</summary>
    private bool IsConfirming => _first.Length == PinLength;

    private string Current => IsConfirming ? _second : _first;

    public string Prompt => IsConfirming ? "RIPETI IL NUOVO PIN" : "SCEGLI IL NUOVO PIN";

    public string Hint => IsConfirming
        ? "Digitalo di nuovo: serve per essere sicuri che non ci sia un errore di battitura."
        : "Quattro cifre. Ti verrà chiesto ogni volta che entri nelle impostazioni.";

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSettingsPage();
    }

    private void OnDigitClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string digit }) return;
        if (Current.Length >= PinLength) return;

        if (IsConfirming) _second += digit;
        else _first += digit;

        StatusLine.Text = string.Empty;
        Refresh();

        if (_second.Length == PinLength)
            Commit();
    }

    private void OnBackspaceClick(object? sender, RoutedEventArgs e)
    {
        // Backing out of the empty confirmation returns to editing the first entry,
        // rather than stranding you on a screen where nothing responds.
        if (IsConfirming && _second.Length == 0)
        {
            _first = _first[..^1];
            Refresh();
            return;
        }

        if (Current.Length == 0) return;

        if (IsConfirming) _second = _second[..^1];
        else _first = _first[..^1];

        Refresh();
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        _first = string.Empty;
        _second = string.Empty;
        StatusLine.Text = string.Empty;
        Refresh();
    }

    private void Commit()
    {
        if (_first != _second)
        {
            _first = string.Empty;
            _second = string.Empty;
            StatusLine.Foreground = ErrorBrush;
            StatusLine.Text = "I due PIN non coincidono. Riprova.";
            Refresh();
            return;
        }

        AppConfigService.Instance.Config.SettingsPin = _first;
        AppConfigService.Instance.SaveConfig("settings-pin");

        _first = string.Empty;
        _second = string.Empty;
        StatusLine.Foreground = OkBrush;
        StatusLine.Text = "PIN aggiornato. Da adesso entri nelle impostazioni con questo.";
        Refresh();
    }

    private static readonly IBrush OkBrush = new SolidColorBrush(Color.Parse("#7FE0A8"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FF8A8A"));

    private void Refresh()
    {
        UpdateDots();
        OnPropertyChanged(nameof(Prompt));
        OnPropertyChanged(nameof(Hint));
    }

    /// <summary>Fills a dot per digit typed, the same feedback as the unlock screen.</summary>
    private void UpdateDots()
    {
        var dots = new[] { PinDot1, PinDot2, PinDot3, PinDot4 };
        int filled = Current.Length;

        for (int i = 0; i < dots.Length; i++)
            dots[i].Opacity = i < filled ? 1.0 : 0.2;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
