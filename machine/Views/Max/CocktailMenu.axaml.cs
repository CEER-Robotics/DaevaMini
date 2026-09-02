using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using DaevaMini.Models;
using DaevaMini.Services;
using DaevaMini.ViewModels;

namespace DaevaMini.Views.Max;

public partial class CocktailMenu : UserControl, INotifyPropertyChanged
{
    private const int PageSize = 8;

    /// <summary>Four cards of 400 plus their 15 px margins: one screenful of the strip.</summary>
    private const double PageWidth = 1720;
    private const int CollectMessageMs = 6000;

    /// <summary>How long the "place your glass" prompt waits before giving up.</summary>
    private const int ConfirmTimeoutMs = 15000;

    /// <summary>Track width: the thread of light reaches the end exactly when the pour does.</summary>
    private const double ProgressTrackWidth = 900;
    /// <summary>
    /// How far the strip has to have travelled, on release, to land on the next page
    /// rather than springing back.
    /// </summary>
    private const double SwipeThreshold = 110;

    /// <summary>
    /// A flick: short and quick beats far and slow, so a fast wrist still turns the page
    /// even though the finger barely moved.
    /// </summary>
    private const double FlickThreshold = 45;
    private const int FlickMaxMs = 300;

    /// <summary>Horizontal intent: a drag counts as a swipe only if it out-runs its own drift.</summary>
    private const double SwipeAxisBias = 1.4;

    /// <summary>Resistance past the first and last page, so the end of the strip is felt.</summary>
    private const double EdgeResistance = 3.0;
    private CocktailMenuViewModel? _viewModel;
    private INotifyCollectionChanged? _cocktailsCollection;
    private Point? _swipeStart;

    /// <summary>Set once a press has turned into a horizontal drag: the strip is following the finger.</summary>
    private bool _isDragging;

    /// <summary>Wall clock of the press, for telling a flick from a slow drag.</summary>
    private long _swipeStartedAtMs;

    /// <summary>The strip's transition, parked while dragging so the strip tracks 1:1.</summary>
    private Transitions? _stripTransitions;

    private int _currentPage;
    private FilterTab? _activeFilter;
    private bool _isPouring;
    private bool _isWarningPanelOpen;
    private int _confirmToken;
    private CocktailCardItem? _pourOverlayItem;
    private CocktailCardItem? _tapItem;
    private TapSession? _tapSession;
    private string _pourMessage = string.Empty;
    private bool _isPourDone;

    public CocktailMenu()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PageSurface.AddHandler(PointerPressedEvent, OnSurfacePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        PageSurface.AddHandler(PointerMovedEvent, OnSurfacePointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        PageSurface.AddHandler(PointerReleasedEvent, OnSurfacePointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        PageSurface.AddHandler(PointerCaptureLostEvent, OnSurfacePointerCaptureLost, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Raised when the user taps the back button (return to splash).</summary>
    public event EventHandler<RoutedEventArgs>? BackClicked;

    /// <summary>
    /// Set by the host window: dispenses the cocktail, driving the card progress bar
    /// through the callback it receives.
    /// </summary>
    public Func<Cocktail, Func<int, Task>, Task<bool>>? PourHandler { get; set; }

    /// <summary>Opens the valve for a tap drink. Returns null when it cannot start.</summary>
    public Func<Cocktail, TapSession?>? TapHandler { get; set; }

    public ObservableCollection<CocktailCardItem> VisibleCocktails { get; } = new();

    /// <summary>The menu split into screenfuls; the strip shows them side by side.</summary>
    public ObservableCollection<CocktailPage> Pages { get; } = new();
    public ObservableCollection<PageIndicator> PageIndicators { get; } = new();
    public ObservableCollection<FilterTab> Filters { get; } = new();
    public ObservableCollection<MachineWarning> Warnings { get; } = new();

    public bool HasWarnings => Warnings.Count > 0;

    public bool IsPourOverlayOpen => _pourOverlayItem != null;

    public bool IsTapOverlayOpen => _tapItem != null;
    public string TapTitle => _tapItem?.Cocktail.Title ?? string.Empty;
    public string TapImage => _tapItem?.Cocktail.ImageSource ?? string.Empty;
    public bool IsTapFlowing => _tapSession != null;
    /// <summary>The single word on the button: it names what pressing will do.</summary>
    public string TapButtonLabel => _tapSession == null ? "APRI" : "CHIUDI";
    public string TapHint => _tapSession == null
        ? "Metti il bicchiere sotto al rubinetto"
        : "Premi di nuovo per chiudere";
    public string PourImage => _pourOverlayItem?.Cocktail.ImageSource ?? string.Empty;
    public string PourTitle => _pourOverlayItem?.Cocktail.Title ?? string.Empty;
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
        // The screen going away must not outlive an open valve.
        if (_tapSession is { } open)
        {
            _tapSession = null;
            _ = open.DisposeAsync();
        }

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

        // A tap does not need confirming: the guest is about to work the valve by hand,
        // and the valve screen is itself the decision point. Asking "sei sicuro?" first
        // would put two taps between wanting a beer and getting one.
        if (item.Cocktail.IsTap)
        {
            OpenTapOverlay(item);
            return;
        }

        foreach (var other in VisibleCocktails)
            other.IsConfirming = ReferenceEquals(other, item);

        // The strips stay on the idle color while the guest is choosing: the drink's
        // own color only arrives when the pour actually starts.

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

    private void OpenTapOverlay(CocktailCardItem item)
    {
        _tapItem = item;
        NotifyTap();
    }

    /// <summary>
    /// The one button: opens the valve if closed, closes it if open. Disposing the
    /// session is what actually shuts the valve.
    /// </summary>
    private async void OnTapToggleClick(object? sender, RoutedEventArgs e)
    {
        if (_tapItem is not { } item) return;

        if (_tapSession is { } open)
        {
            _tapSession = null;
            NotifyTap();
            await open.DisposeAsync();
            return;
        }

        if (TapHandler == null) return;

        _tapSession = TapHandler(item.Cocktail);
        if (_tapSession == null)
            SetTapError();
        NotifyTap();
    }

    /// <summary>
    /// Leaves the tap screen. Closes the valve first if it is still open, so walking
    /// away from the screen can never leave beer running.
    /// </summary>
    private async void OnTapDoneClick(object? sender, RoutedEventArgs e)
    {
        var open = _tapSession;
        _tapSession = null;
        _tapItem = null;
        NotifyTap();

        if (open != null)
            await open.DisposeAsync();
    }

    private void SetTapError()
        => Console.WriteLine("[CocktailMenu] The board refused to open the tap");

    private void NotifyTap()
        => Notify(nameof(IsTapOverlayOpen), nameof(TapTitle), nameof(TapImage),
                  nameof(IsTapFlowing), nameof(TapButtonLabel), nameof(TapHint));

    /// <summary>Tells the guest to take the glass before the overlay goes away.</summary>
    private async Task ShowCollectMessage()
    {
        SetPourMessage($"Goditi il tuo {_pourOverlayItem?.Cocktail.Title}!");
        _isPourDone = true;
        Notify(nameof(IsPourDone));
        await Task.Delay(CollectMessageMs);
    }

    private void OpenPourOverlay(CocktailCardItem item)
    {
        _pourOverlayItem = item;
        _isPourDone = false;
        _pourMessage = "EROGAZIONE";
        ProgressFill.Width = 0;
        Notify(nameof(IsPourOverlayOpen), nameof(PourImage), nameof(PourTitle),
               nameof(PourMessage), nameof(IsPourDone), nameof(PourBigName));
    }

    private void ClosePourOverlay()
    {
        _pourOverlayItem = null;
        ProgressFill.Width = 0;
        _isPourDone = false;
        _pourMessage = string.Empty;
        Notify(nameof(IsPourOverlayOpen), nameof(PourImage), nameof(PourTitle),
               nameof(PourMessage), nameof(IsPourDone), nameof(PourBigName));
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

    /// <summary>
    /// Runs the thread of light for the length of the pour. Avalonia interpolates the width
    /// on its own clock: no per-frame property storm, and only a thin strip is repainted.
    /// </summary>
    private async Task AnimateProgress(CocktailCardItem item, int durationMs)
    {
        ProgressFill.Width = 0;

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(Math.Max(durationMs, 1)),
            FillMode = FillMode.Forward,
            Easing = new LinearEasing(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(Layoutable.WidthProperty, 0d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(Layoutable.WidthProperty, ProgressTrackWidth) }
                }
            }
        };

        await animation.RunAsync(ProgressFill);
        ProgressFill.Width = ProgressTrackWidth;
    }

    /// <summary>True when the strip already shows exactly these drinks, in this order.</summary>
    private bool SameCocktails(IReadOnlyList<Cocktail> cocktails)
    {
        if (VisibleCocktails.Count != cocktails.Count)
            return false;

        for (int i = 0; i < cocktails.Count; i++)
        {
            if (!ReferenceEquals(VisibleCocktails[i].Cocktail, cocktails[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Slides the strip so the current page fills the viewport. The Margin transition on
    /// the control turns the jump into a glide, which is why paging feels continuous.
    /// </summary>
    private void ApplyStripOffset() => SetStripOffset(-_currentPage * PageWidth);

    /// <summary>
    /// Moves the strip to an absolute horizontal offset, in design pixels.
    /// </summary>
    /// <remarks>
    /// A render transform rather than a margin: the strip carries every page and every
    /// card, so animating a layout property re-measured the lot on each frame. This only
    /// shifts what has already been drawn, which is what keeps the swipe smooth on the Pi.
    /// </remarks>
    private void SetStripOffset(double x)
    {
        // Without an explicit width the strip measures to zero and nothing is drawn:
        // it has to be as wide as all the pages it carries.
        MenuStrip.Width = Math.Max(Pages.Count, 1) * PageWidth;
        MenuStrip.RenderTransform = TransformOperations.Parse(
            string.Create(CultureInfo.InvariantCulture, $"translateX({x:0.##}px)"));
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
        _swipeStartedAtMs = Environment.TickCount64;
        _isDragging = false;
    }

    /// <summary>
    /// Drags the strip under the finger. Nothing used to move until the finger came off,
    /// which read as "swiping does nothing" and had people jabbing at the pager dots -
    /// so the page now follows the touch and only the landing is animated.
    /// </summary>
    private void OnSurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_swipeStart is not { } start) return;

        Point current = e.GetPosition(PageSurface);
        double dx = current.X - start.X;
        double dy = current.Y - start.Y;

        if (!_isDragging)
        {
            // Wait for the gesture to declare itself: a press that drifts vertically, or
            // barely moves at all, is a tap on a card and must stay one.
            if (Math.Abs(dx) < 12 || Math.Abs(dx) <= Math.Abs(dy) * SwipeAxisBias) return;

            _isDragging = true;

            // Park the glide: while the finger is down the strip must track it exactly,
            // or every move would chase a 420 ms animation and lag behind.
            _stripTransitions = MenuStrip.Transitions;
            MenuStrip.Transitions = null;
        }

        SetStripOffset(-_currentPage * PageWidth + Resisted(dx));
        e.Handled = true;
    }

    /// <summary>
    /// The travel the strip actually makes for a given finger movement. Past the first or
    /// last page it gives only a fraction, so the end of the strip is felt rather than
    /// hit: the page still moves, it just will not follow you into empty space.
    /// </summary>
    private double Resisted(double dx)
    {
        bool pullingPastStart = dx > 0 && _currentPage == 0;
        bool pullingPastEnd = dx < 0 && _currentPage >= TotalPages - 1;

        return pullingPastStart || pullingPastEnd ? dx / EdgeResistance : dx;
    }

    private void OnSurfacePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_swipeStart is not { } start) return;

        Point end = e.GetPosition(PageSurface);
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        long heldMs = Environment.TickCount64 - _swipeStartedAtMs;

        bool wasDragging = _isDragging;
        _swipeStart = null;
        _isDragging = false;

        // Whatever happens next is animated again, including springing back.
        if (wasDragging && _stripTransitions != null)
        {
            MenuStrip.Transitions = _stripTransitions;
            _stripTransitions = null;
        }

        if (TotalPages <= 1 || Math.Abs(dx) <= Math.Abs(dy) * SwipeAxisBias)
        {
            if (wasDragging) ApplyStripOffset();
            return;
        }

        bool far = Math.Abs(dx) > SwipeThreshold;
        bool flicked = heldMs <= FlickMaxMs && Math.Abs(dx) > FlickThreshold;

        if (!far && !flicked)
        {
            // Short of both: fall back to where we started.
            if (wasDragging) ApplyStripOffset();
            return;
        }

        int target = dx < 0 ? _currentPage + 1 : _currentPage - 1;

        // GoToPage does nothing when the page is already the first or last, so the strip
        // still has to be sent home after a pull against the edge.
        if (Math.Clamp(target, 0, TotalPages - 1) == _currentPage)
        {
            if (wasDragging) ApplyStripOffset();
        }
        else
        {
            GoToPage(target);
        }

        e.Handled = true;
    }

    /// <summary>
    /// A drag can end without a release - the pointer leaves the window, or something
    /// else takes the capture - and the strip would be left parked mid-page.
    /// </summary>
    private void OnSurfacePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isDragging && _swipeStart is null) return;

        _swipeStart = null;
        _isDragging = false;

        if (_stripTransitions != null)
        {
            MenuStrip.Transitions = _stripTransitions;
            _stripTransitions = null;
        }

        ApplyStripOffset();
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

        // Cards are rebuilt only when the menu itself changes, so turning a page slides
        // existing cards instead of recreating them.
        if (!SameCocktails(cocktails))
        {
            VisibleCocktails.Clear();
            foreach (var cocktail in cocktails)
                VisibleCocktails.Add(new CocktailCardItem(cocktail));

            Pages.Clear();
            for (int start = 0; start < VisibleCocktails.Count; start += PageSize)
            {
                var page = new CocktailPage(Pages.Count);
                foreach (var item in VisibleCocktails.Skip(start).Take(PageSize))
                    page.Items.Add(item);
                Pages.Add(page);
            }
        }

        ApplyStripOffset();
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
    private static readonly IBrush IdleForeground = new SolidColorBrush(Color.Parse("#8A8F80"));

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

/// <summary>One screenful of the menu: four cards across, two rows.</summary>
public sealed class CocktailPage
{
    public CocktailPage(int index) => Offset = index * 1720d;

    /// <summary>Where this page sits on the strip, in design pixels.</summary>
    public double Offset { get; }

    public ObservableCollection<CocktailCardItem> Items { get; } = new();
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
