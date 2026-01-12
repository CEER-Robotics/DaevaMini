using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Avalonia.Media;
using DaevaMini.ViewModels;

namespace DaevaMini.Views;

public partial class SettingsPage : UserControl
{
    private readonly ModesViewModel _modesViewModel;
    private readonly ModesViewModel _localViewModel;

    public SettingsPage(ModesViewModel modesViewModel)
    {
        InitializeComponent();
        _modesViewModel = modesViewModel;
        _localViewModel = new ModesViewModel
        {
            CurrentMode = _modesViewModel.CurrentMode
        };

        DataContext = _localViewModel;
        _localViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ModesViewModel.CurrentMode))
            {
                UpdateModeButtons();
            }
        };
        
        // Update iniziale
        Loaded += (s, e) => UpdateModeButtons();
    }

    private void ModeSelectorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && 
            button.Tag is Mode mode && 
            DataContext is ModesViewModel viewModel)
        {
            viewModel.CurrentMode = mode;
        }
    }

    private void UpdateModeButtons()
    {
        if (DataContext is not ModesViewModel vm) return;

        // Trova tutti i bottoni nel visual tree
        var itemsControl = this.FindControl<ItemsControl>("ModesItemsControl");
        if (itemsControl?.ItemsSource == null) return;

        foreach (var container in itemsControl.GetRealizedContainers())
        {
            if (container is ContentPresenter presenter &&
                presenter.Child is Button button &&
                button.Tag is Mode mode)
            {
                var grid = button.Content as Grid;
                var border = grid?.Children.OfType<Border>().FirstOrDefault(b => b.Name == "ActiveBackground");
                var textBlock = grid?.Children.OfType<TextBlock>().FirstOrDefault();

                if (border != null && textBlock != null)
                {
                    bool isActive = mode.Name == vm.CurrentMode.Name;
                    border.IsVisible = isActive;
                    textBlock.Foreground = isActive ? Brushes.Black : new SolidColorBrush(Color.Parse("#888888"));
                }
            }
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _modesViewModel.CurrentMode = _localViewModel.CurrentMode;

        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowContainerSetupPage();
    }

    private void OnBackArrow(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSplash();
    }
}