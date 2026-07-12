using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DaevaMini.Models;
using DaevaMini.ViewModels;

namespace DaevaMini.Views.Max;

public partial class CocktailMenu : UserControl, INotifyPropertyChanged
{
    private const int PageSize = 8;
    private const double SwipeThreshold = 140;
    private const double SwipeAxisBias = 1.4;
    private CocktailMenuViewModel? _viewModel;
    private INotifyCollectionChanged? _cocktailsCollection;
    private Point? _swipeStart;
    private int _currentPage;

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

    public ObservableCollection<Cocktail> VisibleCocktails { get; } = new();
    public ObservableCollection<PageIndicator> PageIndicators { get; } = new();

    public bool IsPagerVisible => TotalPages > 1;
    public new event PropertyChangedEventHandler? PropertyChanged;

    private int TotalPages
    {
        get
        {
            int count = _viewModel?.Cocktails.Count ?? 0;
            return Math.Max(1, (int)Math.Ceiling(count / (double)PageSize));
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (this.FindControl<Button>("BackButton") is { } backBtn)
            backBtn.Click += (_, args) => BackClicked?.Invoke(this, args);
        RefreshPage();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
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
        _currentPage = Math.Min(_currentPage, TotalPages - 1);
        RefreshPage();
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
        IReadOnlyList<Cocktail> cocktails = _viewModel?.Cocktails ?? [];
        int totalPages = TotalPages;
        _currentPage = Math.Clamp(_currentPage, 0, totalPages - 1);

        VisibleCocktails.Clear();
        foreach (var cocktail in cocktails.Skip(_currentPage * PageSize).Take(PageSize))
            VisibleCocktails.Add(cocktail);

        PageIndicators.Clear();
        for (int i = 0; i < totalPages; i++)
            PageIndicators.Add(new PageIndicator(i, i == _currentPage));

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPagerVisible)));
    }
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
