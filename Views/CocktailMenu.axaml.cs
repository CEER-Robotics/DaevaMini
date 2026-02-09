using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DaevaMini.Views;

public partial class CocktailMenu : UserControl
{
    public CocktailMenu()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user taps the back button (return to splash).</summary>
    public event EventHandler<RoutedEventArgs>? BackClicked;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (this.FindControl<Button>("BackButton") is { } backBtn)
            backBtn.Click += (_, args) => BackClicked?.Invoke(this, args);
    }
}
