using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DaevaMini.ViewModels;

namespace DaevaMini.Views.Mini;

public partial class LockScreen : UserControl
{
    public LockScreen()
    {
        InitializeComponent();
    }

    public LockScreen(ModesViewModel modesViewModel) : this()
    {
        DataContext = modesViewModel;
    }

    public event EventHandler<RoutedEventArgs>? StartClicked;
    public event EventHandler<RoutedEventArgs>? SettingsClicked;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (this.FindControl<Button>("StartButton") is { } btn)
            btn.Click += (_, args) => StartClicked?.Invoke(this, args);
        if (this.FindControl<Button>("SettingsButton") is { } gear)
            gear.Click += (_, args) => SettingsClicked?.Invoke(this, args);
    }
}
