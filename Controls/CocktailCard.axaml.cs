using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DaevaMini.Controls;

public partial class CocktailCard : UserControl
{
    public CocktailCard()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user taps "Dale".</summary>
    public event EventHandler<RoutedEventArgs>? DaleClicked;

    /// <summary>Raised when the user taps the close button.</summary>
    public event EventHandler<RoutedEventArgs>? CloseClicked;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (this.FindControl<Button>("CloseButton") is { } closeBtn)
            closeBtn.Click += (_, args) => CloseClicked?.Invoke(this, args);
        if (this.FindControl<Button>("DaleButton") is { } daleBtn)
            daleBtn.Click += (_, args) => DaleClicked?.Invoke(this, args);
    }
}
