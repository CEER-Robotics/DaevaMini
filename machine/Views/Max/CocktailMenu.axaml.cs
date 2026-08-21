using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.Media;
using DaevaMini.Models;
using DaevaMini.Services;
using DaevaMini.ViewModels;

namespace DaevaMini.Views.Max;

public partial class CocktailMenu : UserControl, INotifyPropertyChanged
{
    private const int PageSize = 8;
    private const int CollectMessageMs = 6000;

    /// <summary>How long the "place your glass" prompt waits before giving up.</summary>
    private const int ConfirmTimeoutMs = 15000;
    private const double SwipeThreshold = 140;
    private const double SwipeAxisBias = 1.4;
    private CocktailMenuViewModel? _viewModel;
    private INotifyCollectionChanged? _cocktailsCollection;
    private Point? _swipeStart;
    private int _currentPage;
    private FilterTab? _activeFilter;
    private bool _isPouring;
    private bool _isWarningPanelOpen;
    private int _confirmToken;
    private CocktailCardItem? _pourOverlayItem;
    private double _pourSweepAngle;
    private string _pourMessage = string.Empty;
    private bool _isPourDone;

    public CocktailMenu()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PageSurface.AddHandler(PointerPressedEvent, OnSurfacePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        PageSurface.AddHandler(PointerMovedEvent, OnSurfacePointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        PageSurface.AddHandler(PointerReleasedEvent, OnSurfacePointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Raised when the user taps the back button (return to splash).</summary>
    public event EventHandler<RoutedEventArgs>? BackClicked;

    /// <summary>
    /// Set by the host window: dispenses the cocktail, driving the card progress bar
    /// through the callback it receives.
    /// </summary>
    public Func<Cocktail, Func<int, Task>, Task<bool>>? PourHandler { get; set; }

    public ObservableCollection<CocktailCardItem> VisibleCocktails { get; } = new();
    public ObservableCollection<PageIndicator> PageIndicators { get; } = new();
    public ObservableCollection<FilterTab> Filters { get; } = new();
    public ObservableCollection<MachineWarning> Warnings { get; } = new();

    public bool HasWarnings => Warnings.Count > 0;

    public bool IsPourOverlayOpen => _pourOverlayItem != null;
    public string PourImage => _pourOverlayItem?.Cocktail.ImageSource ?? string.Empty;
    public string PourTitle => _pourOverlayItem?.Cocktail.Title ?? string.Empty;
    public double PourSweepAngle => _pourSweepAngle;
    public string PourMessage => _pourMessage;

    /// <summary>The drink name closing the "Enjoy Your ..." line, set larger than the rest.</summary>
    public string PourBigName => _pourOverlayItem is { } item ? $"{item.Cocktail.Title}!" : string.Empty;

    /// <summary>True once the drink is poured: the label stops blinking and the arrow appears.</summary>
    public bool IsPourDone => _isPourDone;

    /// <summary>The badge takes the colour of the most severe warning on the list.</summary>
    public IBrush WarningAccent => Warnings.Any(w => w.Level == WarningLevel.Critical)
        ? MachineWarning.CriticalBrush
        : MachineWarning.WarningBrush;
    public bool IsWarningPanelOpen => _isWarningPanelOpen;

    public bool IsPagerVisible => TotalPages > 1;
    public new event PropertyChangedEventHandler? PropertyChanged;

    private int TotalPages
    {
        get
        {
            int count = FilteredCocktails.Count;
            return Math.Max(1, (int)Math.Ceiling(count / (double)PageSize));
        }
    }

    /// <summary>Cocktails matching the active filter tab; every cocktail when Home is active.</summary>
    private IReadOnlyList<Cocktail> FilteredCocktails
    {
        get
        {
            IReadOnlyList<Cocktail> all = _viewModel?.Cocktails ?? [];
            string? category = _activeFilter?.Category;
            return category == null
                ? all
                : all.Where(c => c.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (this.FindControl<Button>("BackButton") is { } backBtn)
            backBtn.Click += (_, args) => BackClicked?.Invoke(this, args);

        MachineWarningService.Instance.WarningsChanged += OnWarningsChanged;
        MachineWarningService.Instance.Refresh();
        RefreshWarnings();
        RefreshPage();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        MachineWarningService.Instance.WarningsChanged -= OnWarningsChanged;
        UnsubscribeFromViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnsubscribeFromViewModel();
        _viewModel = DataContext as CocktailMenuViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SubscribeToCocktailsCollection();
        }

        _currentPage = 0;
        RebuildFilters();
        RefreshPage();
    }

    private void UnsubscribeFromViewModel()
    {
        if (_cocktailsCollection != null)
        {
            _cocktailsCollection.CollectionChanged -= OnCocktailsChanged;
            _cocktailsCollection = null;
        }

        if (_viewModel != null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CocktailMenuViewModel.Cocktails))
        {
            SubscribeToCocktailsCollection();
            RebuildFilters();
            _currentPage = Math.Min(_currentPage, TotalPages - 1);
            RefreshPage();
        }
    }

    private void SubscribeToCocktailsCollection()
    {
        if (_cocktailsCollection != null)
            _cocktailsCollection.CollectionChanged -= OnCocktailsChanged;

        _cocktailsCollection = _viewModel?.Cocktails;
        if (_cocktailsCollection != null)
            _cocktailsCollection.CollectionChanged += OnCocktailsChanged;
    }

    private void OnCocktailsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildFilters();
        _currentPage = Math.Min(_currentPage, TotalPages - 1);
        RefreshPage();
    }

    private void OnFilterClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: FilterTab tab }) return;
        if (ReferenceEquals(tab, _activeFilter)) return;

        SetActiveFilter(tab);
        _currentPage = 0;
        RefreshPage();
    }

    private void SetActiveFilter(FilterTab? tab)
    {
        _activeFilter = tab;
        foreach (var filter in Filters)
            filter.IsSelected = ReferenceEquals(filter, tab);
    }

    /// <summary>
    /// Rebuilds the filter bar from the categories present in the current cocktail list,
    /// keeping the active tab selected when its category is still available.
    /// </summary>
    private void RebuildFilters()
    {
        var categories = (_viewModel?.Cocktails ?? [])
            .Select(c => c.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? previous = _activeFilter?.Category;

        Filters.Clear();
        Filters.Add(new FilterTab("Home", null));
        foreach (string category in categories)
            Filters.Add(new FilterTab(category, category));

        FilterTab restored = Filters.FirstOrDefault(f =>
            string.Equals(f.Category, previous, StringComparison.OrdinalIgnoreCase)) ?? Filters[0];
        SetActiveFilter(restored);
    }

    private void OnWarningClick(object? sender, RoutedEventArgs e)
    {
        if (Warnings.Count == 0) return;

        _isWarningPanelOpen = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWarningPanelOpen)));
    }

    private void OnWarningPanelClose(object? sender, RoutedEventArgs e)
    {
        _isWarningPanelOpen = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWarningPanelOpen)));
    }

    private void RefreshWarnings()
    {
        Warnings.Clear();
        // Critical first: an empty bottle matters more than one running low.
        foreach (var warning in MachineWarningService.Instance.Warnings
                     .OrderByDescending(w => w.Level)
                     .ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase))
            Warnings.Add(warning);

        if (Warnings.Count == 0 && _isWarningPanelOpen)
        {
            _isWarningPanelOpen = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWarningPanelOpen)));
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasWarnings)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WarningAccent)));
    }

    private void OnWarningsChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(RefreshWarnings);

    private async void OnPourClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: CocktailCardItem item }) return;
        if (_isPouring) return;

        foreach (var other in VisibleCocktails)
            other.IsConfirming = ReferenceEquals(other, item);

        // Nothing cancels the prompt, so it steps back on its own if nobody confirms.
        int token = ++_confirmToken;
        await Task.Delay(ConfirmTimeoutMs);
        if (token == _confirmToken && item.IsConfirming && !item.IsPouring)
            item.IsConfirming = false;
    }

    private void OnPourCancelClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: CocktailCardItem item }) return;

        item.IsConfirming = false;
    }

    private async void OnPourConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: CocktailCardItem item }) return;
        if (_isPouring) return;

        item.IsConfirming = false;
        _confirmToken++;
        if (PourHandler == null) return;

        _isPouring = true;
        item.IsPouring = true;
        OpenPourOverlay(item);
        bool poured;
        try
        {
            poured = await PourHandler(item.Cocktail, duration => AnimateProgress(item, duration));
        }
        finally
        {
            item.IsPouring = false;
            item.Progress = 0;
            _isPouring = false;
        }

        if (poured)
            await ShowCollectMessage();

        ClosePourOverlay();
    }

    /// <summary>Tells the guest to take the glass before the overlay goes away.</summary>
    private async Task ShowCollectMessage()
    {
        SetPourMessage("Goditi il tuo");
        SetPourSweep(360);
        _isPourDone = true;
        Notify(nameof(IsPourDone));
        await Task.Delay(CollectMessageMs);
    }

    private void OpenPourOverlay(CocktailCardItem item)
    {
        _pourOverlayItem = item;
        _pourSweepAngle = 0;
        _isPourDone = false;
        _pourMessage = "Erogazione in corso";
        Notify(nameof(IsPourOverlayOpen), nameof(PourImage), nameof(PourTitle),
               nameof(PourSweepAngle), nameof(PourMessage), nameof(IsPourDone),
               nameof(PourBigName));
    }

    private void ClosePourOverlay()
    {
        _pourOverlayItem = null;
        _pourSweepAngle = 0;
        _isPourDone = false;
        _pourMessage = string.Empty;
        Notify(nameof(IsPourOverlayOpen), nameof(PourImage), nameof(PourTitle),
               nameof(PourSweepAngle), nameof(PourMessage), nameof(IsPourDone),
               nameof(PourBigName));
    }

    private void SetPourSweep(double degrees)
    {
        _pourSweepAngle = degrees;
        Notify(nameof(PourSweepAngle));
    }

    private void SetPourMessage(string message)
    {
        _pourMessage = message;
        Notify(nameof(PourMessage));
    }

    private void Notify(params string[] names)
    {
        foreach (string name in names)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private async Task AnimateProgress(CocktailCardItem item, int durationMs)
    {
        const int updateIntervalMs = 50;
        int elapsed = 0;
        item.Progress = 0;
        SetPourSweep(0);
        while (elapsed < durationMs)
        {
            await Task.Delay(updateIntervalMs);
            elapsed += updateIntervalMs;
            double percent = Math.Min(100.0 * elapsed / durationMs, 100);
            item.Progress = percent;
            SetPourSweep(percent * 3.6);
        }
    }

    private void OnPageIndicatorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: int page }) return;
        if (page < 0 || page >= TotalPages || page == _currentPage) return;

        GoToPage(page);
    }

    private void OnSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TotalPages <= 1) return;

        _swipeStart = e.GetPosition(PageSurface);
    }

    private void OnSurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_swipeStart is not { } start) return;

        Point current = e.GetPosition(PageSurface);
        double dx = current.X - start.X;
        double dy = current.Y - start.Y;
        if (Math.Abs(dx) > SwipeThreshold && Math.Abs(dx) > Math.Abs(dy) * SwipeAxisBias)
        {
            e.Handled = true;
        }
    }

    private void OnSurfacePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_swipeStart is not { } start)
            return;

        Point end = e.GetPosition(PageSurface);
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        _swipeStart = null;

        if (TotalPages <= 1 || Math.Abs(dx) <= SwipeThreshold || Math.Abs(dx) <= Math.Abs(dy) * SwipeAxisBias)
        {
            return;
        }

        if (dx < 0)
            GoToPage(_currentPage + 1);
        else
            GoToPage(_currentPage - 1);

        e.Handled = true;
    }

    private void GoToPage(int page)
    {
        int clampedPage = Math.Clamp(page, 0, TotalPages - 1);
        if (clampedPage == _currentPage) return;

        _currentPage = clampedPage;
        RefreshPage();
    }

    private void RefreshPage()
    {
        IReadOnlyList<Cocktail> cocktails = FilteredCocktails;
        int totalPages = TotalPages;
        _currentPage = Math.Clamp(_currentPage, 0, totalPages - 1);

        VisibleCocktails.Clear();
        foreach (var cocktail in cocktails.Skip(_currentPage * PageSize).Take(PageSize))
            VisibleCocktails.Add(new CocktailCardItem(cocktail));

        PageIndicators.Clear();
        for (int i = 0; i < totalPages; i++)
            PageIndicators.Add(new PageIndicator(i, i == _currentPage));

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPagerVisible)));
    }
}

/// <summary>
/// A cocktail as shown in the menu grid, plus the per-card state the grid needs:
/// whether it is asking for pour confirmation and whether it is currently pouring.
/// </summary>
public sealed class CocktailCardItem : INotifyPropertyChanged
{
    private bool _isConfirming;
    private bool _isPouring;
    private bool _isCompleted;
    private double _progress;

    public CocktailCardItem(Cocktail cocktail) => Cocktail = cocktail;

    public Cocktail Cocktail { get; }

    public bool IsConfirming
    {
        get => _isConfirming;
        set
        {
            if (_isConfirming == value) return;
            _isConfirming = value;
            OnPropertyChanged(nameof(IsConfirming));
            OnPropertyChanged(nameof(IsPourButtonVisible));
        }
    }

    public bool IsPouring
    {
        get => _isPouring;
        set
        {
            if (_isPouring == value) return;
            _isPouring = value;
            OnPropertyChanged(nameof(IsPouring));
            OnPropertyChanged(nameof(IsPourButtonVisible));
        }
    }

    /// <summary>True while the "collect your drink" message is showing, right after a pour.</summary>
    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (_isCompleted == value) return;
            _isCompleted = value;
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(IsPourButtonVisible));
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (Math.Abs(_progress - value) < 0.01) return;
            _progress = value;
            OnPropertyChanged(nameof(Progress));
        }
    }

    /// <summary>The pour button only shows on an active card that is idle.</summary>
    public bool IsPourButtonVisible => Cocktail.IsActive && !_isConfirming && !_isPouring && !_isCompleted;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One tab in the menu filter bar. A null <see cref="Category"/> is the "Home" tab.</summary>
public sealed class FilterTab : INotifyPropertyChanged
{
    private static readonly IBrush SelectedBackground = new SolidColorBrush(Color.Parse("#2A3030"));
    private static readonly IBrush SelectedForeground = new SolidColorBrush(Color.Parse("#E7E6DC"));
    private static readonly IBrush IdleForeground = new SolidColorBrush(Color.Parse("#888888"));

    private bool _isSelected;

    public FilterTab(string name, string? category)
    {
        Name = name;
        Category = category;
    }

    public string Name { get; }
    public string? Category { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(Background));
            OnPropertyChanged(nameof(Foreground));
            OnPropertyChanged(nameof(FontWeight));
        }
    }

    public IBrush Background => _isSelected ? SelectedBackground : Brushes.Transparent;
    public IBrush Foreground => _isSelected ? SelectedForeground : IdleForeground;
    public FontWeight FontWeight => _isSelected ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Medium;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class PageIndicator
{
    public PageIndicator(int index, bool isActive)
    {
        Index = index;
        Width = isActive ? 32 : 12;
        Opacity = isActive ? 0.9 : 0.3;
    }

    public int Index { get; }
    public double Width { get; }
    public double Opacity { get; }
}
