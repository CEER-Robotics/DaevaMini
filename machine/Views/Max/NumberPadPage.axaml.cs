using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using DaevaMini.Config;

namespace DaevaMini.Views.Max;

public partial class NumberPadPage : UserControl
{
    private const int PinLength = 4;
    private readonly string _settingsPin;
    private readonly Ellipse[] _pinDots;
    private string _enteredPin = string.Empty;

    public NumberPadPage()
    {
        InitializeComponent();
        _settingsPin = NormalizePin(AppConfigService.Instance.Config.SettingsPin);
        _pinDots = [PinDot1, PinDot2, PinDot3, PinDot4];
        UpdatePinDots();
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSplash();
    }

    private void Num_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string digit } || _enteredPin.Length >= PinLength)
            return;

        _enteredPin += digit;
        PromptText.Text = "ENTER PIN";
        PromptText.Foreground = Brush.Parse("#E7E6DC");
        UpdatePinDots();

        if (_enteredPin.Length == PinLength)
            ValidatePin();
    }

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        ClearPin();
    }

    private void Del_Click(object? sender, RoutedEventArgs e)
    {
        if (_enteredPin.Length == 0)
            return;

        _enteredPin = _enteredPin[..^1];
        PromptText.Text = "ENTER PIN";
        PromptText.Foreground = Brush.Parse("#E7E6DC");
        UpdatePinDots();
    }

    private void ValidatePin()
    {
        if (_enteredPin == _settingsPin)
        {
            if (VisualRoot is MainWindow mainWindow)
                mainWindow.ShowSettingsPage();
            return;
        }

        PromptText.Text = "WRONG PIN";
        PromptText.Foreground = Brush.Parse("#FF8A8A");
        ClearPin();
    }

    private void ClearPin()
    {
        _enteredPin = string.Empty;
        UpdatePinDots();
    }

    private void UpdatePinDots()
    {
        for (int i = 0; i < _pinDots.Length; i++)
        {
            bool isFilled = i < _enteredPin.Length;
            _pinDots[i].Fill = Brush.Parse(isFilled ? "#CAF0F8" : "#E7E6DC");
            _pinDots[i].Opacity = isFilled ? 1 : 0.2;
        }
    }

    private static string NormalizePin(string pin)
    {
        var digits = new string(pin.Where(char.IsDigit).Take(PinLength).ToArray());
        return digits.PadRight(PinLength, '0');
    }
}
