using Avalonia.Controls;
using System;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using DaevaMini.ViewModels;

namespace DaevaMini;

public partial class CocktailsMenu : UserControl
{
    public CocktailsMenu()
    {
        Console.WriteLine("[CocktailsMenu] Constructor called");
        InitializeComponent();
        DataContext = new CocktailsMenuViewModel();
    }

    private async void DispenseCocktail(object? sender, RoutedEventArgs e)
    {
        // Fire-and-forget serial write with error handling
        await Task.Run(() =>
        {
            try
            {
                var manager = ArduinoSerialManager.Instance;
                if (manager.IsConnected)
                {
                    Console.WriteLine("[CocktailsMenu] Sending command: c1:2000,c4:4000");
                    bool success = manager.Send("c1:2000,c4:4000");
                    if (!success)
                    {
                        Console.WriteLine("[CocktailsMenu] Failed to send command to Arduino");
                    }
                    else
                    {
                        Console.WriteLine("[CocktailsMenu] Command sent successfully");
                    }
                }
                else
                {
                    Console.WriteLine("[CocktailsMenu] Cannot send command - Arduino is not connected");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CocktailsMenu] Error sending command to Arduino: {ex.Message}");
            }
        });
    }
    
    private void OnBackArrow(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSplash();
    }

    // No cleanup needed - singleton manages its own lifecycle
}
