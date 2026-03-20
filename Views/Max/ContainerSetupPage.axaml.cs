using Avalonia.Controls;
using Avalonia.Interactivity;
using DaevaMini.Config;

namespace DaevaMini.Views.Max;

public partial class ContainerSetupPage : UserControl
{
    public ContainerSetupPage()
    {
        InitializeComponent();
        ApplyLiquidAssignments();
    }

    private void ApplyLiquidAssignments()
    {
        var assignments = AppConfigService.Instance.Config.LiquidAssignments;
        var slots = new[] { Slot0, Slot1, Slot2, Slot3 };
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            slots[i]!.Text = i < assignments.Length && !string.IsNullOrWhiteSpace(assignments[i])
                ? assignments[i]
                : $"Slot {i + 1}";
        }
    }

    private void OnBackArrow(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSettingsPage();
    }

    private void OnDone(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSplash();
    }
}

