using Avalonia.Controls;
using System.IO.Ports;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using DaevaMini.ViewModels;

namespace DaevaMini;

public partial class CocktailsMenu : UserControl
{
    private SerialPort? _serial;
    
    public CocktailsMenu()
    {
        InitializeComponent();
        DataContext = new CocktailsMenuViewModel();
        
        _serial = new SerialPort("/dev/ttyUSB0", 115200)
        {
            NewLine = "\n",
            WriteTimeout = 500
        };

        try
        {
            _serial.Open();
        }
        catch
        {
            _serial = null;
        }
    }

    private async void DispenseCocktail(object? sender, RoutedEventArgs e)
    {
        // Fire-and-forget serial write
        await Task.Run(() =>
        {
            if (_serial?.IsOpen == true)
                _serial.WriteLine("c1:2000,c4:4000");
        });
    }
    
    private void OnBackArrow(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSplash();
    }
}
