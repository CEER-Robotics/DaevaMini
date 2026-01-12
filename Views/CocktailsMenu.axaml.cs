using Avalonia.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DaevaMini.ViewModels;

namespace DaevaMini;

public partial class CocktailsMenu : UserControl
{
    private readonly CocktailsMenuViewModel _viewModel;

    public CocktailsMenu(ModesViewModel modesViewModel)
    {
        Console.WriteLine("[CocktailsMenu] Constructor called");
        InitializeComponent();
        _viewModel = new CocktailsMenuViewModel(modesViewModel);
        DataContext = _viewModel;
    }

    private async void DispenseCocktail(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsDispensing)
            return;

        if (sender is not Button button || button.DataContext is not CocktailVm cocktail)
        {
            Console.WriteLine("[CocktailsMenu] Invalid cocktail data");
            return;
        }

        _viewModel.IsDispensing = true;

        try
        {
            var manager = ArduinoSerialManager.Instance;
            if (!manager.IsConnected)
            {
                Console.WriteLine("[CocktailsMenu] Cannot send command - Arduino is not connected");
                _viewModel.IsDispensing = false;
                return;
            }

            // Map ingredients to channels based on current mode
            var channelDurations = _viewModel.MapIngredientsToChannels(cocktail);

            if (channelDurations.Count == 0)
            {
                Console.WriteLine($"[CocktailsMenu] No matching ingredients found for cocktail {cocktail.Name}");
                _viewModel.IsDispensing = false;
                return;
            }

            // Build command string: c1:duration1,c2:duration2,...
            var commandParts = channelDurations.OrderBy(kvp => kvp.Key)
                .Select(kvp => $"c{kvp.Key}:{kvp.Value}");
            string command = string.Join(",", commandParts);

            Console.WriteLine($"[CocktailsMenu] Sending command: {command}");

            // Send command and wait for DONE
            await Task.Run(async () =>
            {
                bool success = manager.Send(command);
                if (!success)
                {
                    Console.WriteLine("[CocktailsMenu] Failed to send command to Arduino");
                    Dispatcher.UIThread.Post(() => _viewModel.IsDispensing = false);
                    return;
                }

                Console.WriteLine("[CocktailsMenu] Command sent successfully, waiting for DONE...");

                // Wait for DONE message (with timeout)
                const int timeoutMs = 60000; // 60 seconds max
                var startTime = DateTime.Now;
                string? response = null;

                while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                {
                    response = manager.ReadLine();
                    if (response != null)
                    {
                        response = response.Trim();
                        Console.WriteLine($"[CocktailsMenu] Received: {response}");
                        if (response.Equals("DONE", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine("[CocktailsMenu] Dispensing completed");
                            break;
                        }
                    }
                    await Task.Delay(100); // Check every 100ms
                }

                if (response == null || !response.Equals("DONE", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[CocktailsMenu] Timeout waiting for DONE message");
                }

                Dispatcher.UIThread.Post(() => _viewModel.IsDispensing = false);
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CocktailsMenu] Error sending command to Arduino: {ex.Message}");
            _viewModel.IsDispensing = false;
        }
    }
    
    private void OnBackArrow(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSplash();
    }

    // No cleanup needed - singleton manages its own lifecycle
}
