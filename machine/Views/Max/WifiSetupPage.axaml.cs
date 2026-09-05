using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DaevaMini.Services;

namespace DaevaMini.Views.Max;

/// <summary>
/// Scans for Wi-Fi networks and connects to one. All the actual networking goes through
/// <see cref="WifiService"/> (nmcli, since the target is a Raspberry Pi); this page only
/// drives it and shows the result.
/// </summary>
public partial class WifiSetupPage : UserControl, INotifyPropertyChanged
{
    /// <summary>WPA2's own ceiling; nothing typed here can be a real password past this.</summary>
    private const int MaxPasswordLength = 63;

    public WifiSetupPage()
    {
        InitializeComponent();
        _ = RefreshAsync();
    }

    public ObservableCollection<WifiNetworkRow> Networks { get; } = new();

    private bool _scanning;
    public bool IsScanning => _scanning;
    public bool HasNetworks => Networks.Count > 0;
    public bool IsEmpty => !_scanning && Networks.Count == 0;

    public bool IsSimulated => WifiService.IsSimulated;

    private string _statusHeader = "Ricerca reti...";
    public string StatusHeader => _statusHeader;

    /// <summary>
    /// Backs a single bound property instead of a named TextBlock (unlike every other
    /// settings page) because this message has to show up in two different places
    /// depending on state: under the network list normally, inside the password card
    /// while that is open - a named element can only live in one of them.
    /// </summary>
    private string _status = string.Empty;
    public string StatusText => _status;

    private void SetStatus(string message)
    {
        _status = message;
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>Reloads both halves of the header: who we are connected to, and who else
    /// is in range. Run once at startup and again on every manual "CERCA RETI".</summary>
    private async Task RefreshAsync()
    {
        _scanning = true;
        RaiseListChanged();

        var status = await WifiService.GetStatusAsync();
        _statusHeader = status.Connected
            ? $"Connesso a \"{status.Ssid}\"" + (status.IpAddress != null ? $" · {status.IpAddress}" : string.Empty)
            : "Nessuna rete collegata";

        var found = await WifiService.ScanAsync();

        Networks.Clear();
        foreach (var network in found)
            Networks.Add(new WifiNetworkRow(network, isCurrent: status.Connected
                && network.Ssid.Equals(status.Ssid, StringComparison.Ordinal)));

        _scanning = false;
        RaiseListChanged();
    }

    private async void OnRescanClick(object? sender, RoutedEventArgs e) => await RefreshAsync();

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSettingsPage();
    }

    // ------------------------------------------------------------ picking a network

    private WifiNetworkRow? _selected;
    private bool _connecting;

    public bool IsPasswordPanelOpen { get; private set; }
    public bool IsConnecting => _connecting;
    public string PasswordSsid => _selected?.Ssid ?? string.Empty;

    /// <summary>Full sentence rather than a bare label, the way the phone asks it.</summary>
    public string PasswordTitle => $"Inserisci la password per “{PasswordSsid}”";

    private void OnNetworkClick(object? sender, RoutedEventArgs e)
    {
        if (_connecting || sender is not Button { CommandParameter: WifiNetworkRow row }) return;

        SetStatus(string.Empty);
        _selected = row;

        // An open network has nothing to type, so there is nothing to show a keyboard
        // for: go straight to connecting, the same as tapping "CONNETTI" with no password.
        if (!row.Secured)
        {
            _ = ConnectAsync(string.Empty);
            return;
        }

        _password = string.Empty;
        _revealed = false;
        _keyboardMode = KeyboardMode.Letters;
        _shift = true;
        Keys = Cased(true);
        IsPasswordPanelOpen = true;

        RaisePasswordChanged();
        RaiseKeyboardChanged();
    }

    private void OnClosePasswordPanel(object? sender, RoutedEventArgs e)
    {
        if (_connecting) return; // do not strand a live attempt with no way to see it finish

        IsPasswordPanelOpen = false;
        _selected = null;
        RaisePasswordChanged();
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        await ConnectAsync(_password);
    }

    private async Task ConnectAsync(string password)
    {
        if (_selected == null || _connecting) return;
        var target = _selected;

        _connecting = true;
        SetStatus($"Connessione a \"{target.Ssid}\"...");
        RaisePasswordChanged();

        WifiConnectResult result;
        try
        {
            result = await WifiService.ConnectAsync(target.Ssid, password);
        }
        finally
        {
            _connecting = false;
        }

        switch (result)
        {
            case WifiConnectResult.Success:
                IsPasswordPanelOpen = false;
                _selected = null;
                SetStatus($"Connesso a \"{target.Ssid}\".");
                RaisePasswordChanged();
                await RefreshAsync();
                return;

            case WifiConnectResult.WrongPassword:
                SetStatus("Password sbagliata. Riprova.");
                break;

            case WifiConnectResult.NotFound:
                SetStatus($"\"{target.Ssid}\" non si vede più: prova a cercare di nuovo.");
                break;

            default:
                SetStatus("Connessione non riuscita. Riprova fra qualche secondo.");
                break;
        }

        RaisePasswordChanged();
    }

    // ------------------------------------------------------------ on-screen keyboard

    /// <summary>Letters in QWERTY order, same layout as every other keyboard in this app.</summary>
    private const string Layout = "QWERTYUIOPASDFGHJKLZXCVBNM";

    private enum KeyboardMode { Letters, Symbols }

    private KeyboardMode _keyboardMode = KeyboardMode.Letters;
    private string _password = string.Empty;

    /// <summary>
    /// Unlike the naming keyboard elsewhere in this app, shift here is a toggle, not a
    /// one-shot: a Wi-Fi password mixes case all the way through, not just capitalises
    /// a first letter.
    /// </summary>
    private bool _shift = true;

    public bool IsLettersMode => _keyboardMode == KeyboardMode.Letters;
    public bool IsSymbolsMode => _keyboardMode == KeyboardMode.Symbols;
    public string ModeLabel => IsLettersMode ? "123" : "ABC";

    public string[] Keys { get; private set; } = Cased(true);

    private static string[] Cased(bool upper)
        => Layout.Select(c => upper ? c.ToString() : char.ToLowerInvariant(c).ToString()).ToArray();

    public IBrush ShiftBrush => _shift ? ShiftOnBrush : ShiftOffBrush;
    private static readonly IBrush ShiftOnBrush = new SolidColorBrush(Color.Parse("#CAF0F8"));
    private static readonly IBrush ShiftOffBrush = new SolidColorBrush(Color.Parse("#8A8F80"));

    /// <summary>
    /// Hidden behind bullets by default, like a phone: this is typed in front of whoever
    /// is standing at the bar. The eye toggle is there for when a long key gets mistyped
    /// and staring at bullets tells you nothing about where it went wrong.
    /// </summary>
    private bool _revealed;

    public string PasswordDisplay => _revealed ? _password : new string('●', _password.Length);
    public bool HasPassword => _password.Length > 0;
    public bool IsHidden => !_revealed;

    public bool CanConnect => !_connecting && _password.Length > 0;
    public string ConnectButtonLabel => _connecting ? "CONNESSIONE..." : "CONNETTI";

    private void OnToggleReveal(object? sender, RoutedEventArgs e)
    {
        _revealed = !_revealed;
        RaisePasswordChanged();
    }

    private void OnToggleKeyboardMode(object? sender, RoutedEventArgs e)
    {
        _keyboardMode = IsLettersMode ? KeyboardMode.Symbols : KeyboardMode.Letters;
        RaiseKeyboardChanged();
    }

    private void OnShift(object? sender, RoutedEventArgs e)
    {
        _shift = !_shift;
        Keys = Cased(_shift);
        RaiseKeyboardChanged();
    }

    private void OnKeyPress(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } || _password.Length >= MaxPasswordLength) return;

        _password += _shift ? key.ToUpperInvariant() : key.ToLowerInvariant();
        SetStatus(string.Empty);
        RaisePasswordChanged();
    }

    private void OnSymbolPress(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } || _password.Length >= MaxPasswordLength) return;

        _password += key;
        SetStatus(string.Empty);
        RaisePasswordChanged();
    }

    private void OnKeySpace(object? sender, RoutedEventArgs e)
    {
        if (_password.Length >= MaxPasswordLength) return;
        _password += " ";
        RaisePasswordChanged();
    }

    private void OnKeyBackspace(object? sender, RoutedEventArgs e)
    {
        if (_password.Length == 0) return;
        _password = _password[..^1];
        RaisePasswordChanged();
    }

    private void OnKeyClear(object? sender, RoutedEventArgs e)
    {
        _password = string.Empty;
        RaisePasswordChanged();
    }

    // ------------------------------------------------------------ change notification

    private void RaiseListChanged()
    {
        OnPropertyChanged(nameof(IsScanning));
        OnPropertyChanged(nameof(HasNetworks));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(StatusHeader));
        OnPropertyChanged(nameof(IsSimulated));
        OnPropertyChanged(nameof(StatusText));
    }

    private void RaisePasswordChanged()
    {
        OnPropertyChanged(nameof(IsPasswordPanelOpen));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(PasswordSsid));
        OnPropertyChanged(nameof(PasswordTitle));
        OnPropertyChanged(nameof(PasswordDisplay));
        OnPropertyChanged(nameof(HasPassword));
        OnPropertyChanged(nameof(IsHidden));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(ConnectButtonLabel));
        OnPropertyChanged(nameof(StatusText));
    }

    private void RaiseKeyboardChanged()
    {
        OnPropertyChanged(nameof(Keys));
        OnPropertyChanged(nameof(ShiftBrush));
        OnPropertyChanged(nameof(IsLettersMode));
        OnPropertyChanged(nameof(IsSymbolsMode));
        OnPropertyChanged(nameof(ModeLabel));
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One access point in the list, with whether it is the one already in use.</summary>
public sealed class WifiNetworkRow : INotifyPropertyChanged
{
    public WifiNetworkRow(WifiNetwork network, bool isCurrent)
    {
        Network = network;
        _isCurrent = isCurrent;
    }

    public WifiNetwork Network { get; }
    public string Ssid => Network.Ssid;
    public bool Secured => Network.Secured;
    public string SignalLabel => $"{Network.SignalPercent}%";

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value) return;
            _isCurrent = value;
            OnPropertyChanged(nameof(IsCurrent));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
