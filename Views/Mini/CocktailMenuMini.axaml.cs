using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DaevaMini.Views.Mini;

public partial class CocktailMenuMini : UserControl
{
    public CocktailMenuMini()
    {
        InitializeComponent();
    }

    public event EventHandler<RoutedEventArgs>? BackClicked;
    public event EventHandler<RoutedEventArgs>? SettingsClicked;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (this.FindControl<Button>("BackButton") is { } backBtn)
            backBtn.Click += (_, args) => BackClicked?.Invoke(this, args);
        if (this.FindControl<Button>("SettingsButton") is { } settingsBtn)
            settingsBtn.Click += (_, args) => SettingsClicked?.Invoke(this, args);
    }
}
