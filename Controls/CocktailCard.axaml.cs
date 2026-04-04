using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DaevaMini.Controls;

public partial class CocktailCard : UserControl
{
    private Button? _daleBtn;
    private Button? _closeBtn;
    private ProgressBar? _progressBar;

    public CocktailCard()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user taps "Start".</summary>
    public event EventHandler<RoutedEventArgs>? DaleClicked;

    /// <summary>Raised when the user taps the close button.</summary>
    public event EventHandler<RoutedEventArgs>? CloseClicked;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _closeBtn = this.FindControl<Button>("CloseButton");
        _daleBtn = this.FindControl<Button>("DaleButton");
        _progressBar = this.FindControl<ProgressBar>("DispenseProgress");

        if (_closeBtn == null) Console.WriteLine("[CocktailCard] WARNING: CloseButton not found");
        if (_daleBtn == null) Console.WriteLine("[CocktailCard] WARNING: DaleButton not found");
        if (_progressBar == null) Console.WriteLine("[CocktailCard] WARNING: DispenseProgress not found");

        if (_closeBtn != null) _closeBtn.Click += (_, args) => CloseClicked?.Invoke(this, args);
        if (_daleBtn != null) _daleBtn.Click += (_, args) => DaleClicked?.Invoke(this, args);
    }

    /// <summary>
    /// Swaps the Start button for an animated progress bar for the given duration,
    /// then restores the button. The close button is disabled while dispensing.
    /// </summary>
    public async Task StartDispensing(int durationMs)
    {
        if (_daleBtn != null) _daleBtn.IsVisible = false;
        if (_closeBtn != null) _closeBtn.IsEnabled = false;

        if (_progressBar != null)
        {
            _progressBar.Value = 0;
            _progressBar.IsVisible = true;

            const int updateIntervalMs = 50;
            int elapsed = 0;
            while (elapsed < durationMs)
            {
                await Task.Delay(updateIntervalMs);
                elapsed += updateIntervalMs;
                _progressBar.Value = Math.Min(100.0 * elapsed / durationMs, 100);
            }

            _progressBar.IsVisible = false;
        }

        if (_daleBtn != null) _daleBtn.IsVisible = true;
        if (_closeBtn != null) _closeBtn.IsEnabled = true;
    }
}
