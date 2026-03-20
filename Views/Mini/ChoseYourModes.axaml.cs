using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DaevaMini.Views.Mini;

public partial class ChoseYourModes : UserControl
{
    public ChoseYourModes()
    {
        InitializeComponent();
    }

    public event EventHandler<RoutedEventArgs>? BackClicked;
    public event EventHandler<RoutedEventArgs>? DoneClicked;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        BackBtn.Click += (_, args) => BackClicked?.Invoke(this, args);
        DoneBtn.Click += (_, args) => DoneClicked?.Invoke(this, args);
    }
}
