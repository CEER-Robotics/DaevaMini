using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DaevaMini.ViewModels;

namespace DaevaMini.Views.Mini;

public partial class SettingsMini : UserControl
{
    private readonly ModesViewModel? _modesViewModel;

    public SettingsMini()
    {
        InitializeComponent();
    }

    public SettingsMini(ModesViewModel modesViewModel) : this()
    {
        _modesViewModel = modesViewModel;
        SyncTabToCurrentMode();
    }

    public event EventHandler<RoutedEventArgs>? BackClicked;
    public event EventHandler<RoutedEventArgs>? FillClicked;
    public event EventHandler<RoutedEventArgs>? CleanClicked;
    public event EventHandler<RoutedEventArgs>? EditModesClicked;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (this.FindControl<Button>("BackBtn") is { } backBtn)
            backBtn.Click += (_, args) => BackClicked?.Invoke(this, args);
        if (this.FindControl<Button>("FillStartBtn") is { } fillBtn)
            fillBtn.Click += (_, args) => FillClicked?.Invoke(this, args);
        if (this.FindControl<Button>("CleanStartBtn") is { } cleanBtn)
            cleanBtn.Click += (_, args) => CleanClicked?.Invoke(this, args);
        if (this.FindControl<Button>("EditModesBtn") is { } editBtn)
            editBtn.Click += (_, args) => EditModesClicked?.Invoke(this, args);

        var ginTab = this.FindControl<RadioButton>("GinModeTab");
        var vodkaTab = this.FindControl<RadioButton>("VodkaModeTab");
        var ogTab = this.FindControl<RadioButton>("OGModeTab");

        if (ginTab != null) ginTab.IsCheckedChanged += (_, _) => { if (ginTab.IsChecked == true) SetMode("Gin Mode"); };
        if (vodkaTab != null) vodkaTab.IsCheckedChanged += (_, _) => { if (vodkaTab.IsChecked == true) SetMode("Vodka Mode"); };
        if (ogTab != null) ogTab.IsCheckedChanged += (_, _) => { if (ogTab.IsChecked == true) SetMode("OG Mode"); };
    }

    private void SetMode(string modeName)
    {
        if (_modesViewModel == null) return;
        var mode = ModesViewModel.Modes.FirstOrDefault(m =>
            m.Name.Equals(modeName, StringComparison.OrdinalIgnoreCase));
        if (mode != null)
            _modesViewModel.CurrentMode = mode;
    }

    private void SyncTabToCurrentMode()
    {
        if (_modesViewModel == null) return;

        var ginTab = this.FindControl<RadioButton>("GinModeTab");
        var vodkaTab = this.FindControl<RadioButton>("VodkaModeTab");
        var ogTab = this.FindControl<RadioButton>("OGModeTab");

        var name = _modesViewModel.CurrentModeName;
        if (name.Equals("Gin Mode", StringComparison.OrdinalIgnoreCase) && ginTab != null)
            ginTab.IsChecked = true;
        else if (name.Equals("Vodka Mode", StringComparison.OrdinalIgnoreCase) && vodkaTab != null)
            vodkaTab.IsChecked = true;
        else if (name.Equals("OG Mode", StringComparison.OrdinalIgnoreCase) && ogTab != null)
            ogTab.IsChecked = true;
    }
}
