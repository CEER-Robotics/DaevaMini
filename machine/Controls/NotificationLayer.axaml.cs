using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using DaevaMini.Services;

namespace DaevaMini.Controls;

/// <summary>
/// The machine's notification surface, mounted once over the page container so it
/// outlives every page change. Listens to <see cref="MachineNotificationService"/> and
/// nothing else; it has no idea what a container or a pump is.
/// </summary>
public partial class NotificationLayer : UserControl
{
    /// <summary>How long a banner stays before it sees itself out.</summary>
    private static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(7);

    /// <summary>Time for the slide-out to finish before the item is actually dropped.</summary>
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(450);

    /// <summary>
    /// Above this the banners start eating the screen. Three is also about as many as
    /// anyone reads at a glance; older ones leave early to make room.
    /// </summary>
    private const int MaxToasts = 3;

    /// <summary>How many faults may wait behind the one being read.</summary>
    private const int MaxQueuedAlerts = 2;

    /// <summary>Where a banner sits before it has slid in, and where it goes to leave.</summary>
    private static readonly Thickness OffScreen = new(0, -200, 0, 0);
    private static readonly Thickness OnScreen = new(0, 0, 0, 14);

    /// <summary>
    /// Alerts are modal, so a second fault arriving while one is up has to wait rather
    /// than overwrite the one being read.
    /// </summary>
    private readonly Queue<MachineNotification> _pendingAlerts = new();

    private bool _alertOpen;

    public NotificationLayer()
    {
        InitializeComponent();
    }

    public ObservableCollection<ToastItem> Toasts { get; } = new();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        MachineNotificationService.Instance.Posted += OnPosted;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        MachineNotificationService.Instance.Posted -= OnPosted;
        base.OnUnloaded(e);
    }

    private void OnPosted(object? sender, MachineNotification notification)
    {
        if (notification.Style == NotificationStyle.Alert)
            ShowAlert(notification);
        else
            ShowToast(notification);
    }

    // ------------------------------------------------------------------- banners

    private void ShowToast(MachineNotification notification)
    {
        while (Toasts.Count >= MaxToasts)
            DismissToast(Toasts[0]);

        var item = new ToastItem(notification) { Offset = OffScreen, Opacity = 0 };
        Toasts.Add(item);

        // One frame later, so the control exists with the off-screen margin already
        // applied and the transition has something to animate from. Setting both in the
        // same pass would just render it in place.
        Dispatcher.UIThread.Post(() =>
        {
            if (!Toasts.Contains(item)) return;
            item.Offset = OnScreen;
            item.Opacity = 1;
        }, DispatcherPriority.Background);

        item.Timer = new DispatcherTimer { Interval = ToastLifetime };
        item.Timer.Tick += (_, _) => DismissToast(item);
        item.Timer.Start();
    }

    private void OnToastPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: ToastItem item })
            DismissToast(item);
    }

    private void DismissToast(ToastItem item)
    {
        if (item.IsLeaving) return;
        item.IsLeaving = true;
        item.Timer?.Stop();
        item.Timer = null;

        item.Offset = OffScreen;
        item.Opacity = 0;

        // Removed only once it is out of sight, otherwise the banner vanishes mid-slide
        // and the ones below it snap upwards.
        DispatcherTimer.RunOnce(() => Toasts.Remove(item), ExitDuration);
    }

    // -------------------------------------------------------------------- alerts

    private void ShowAlert(MachineNotification notification)
    {
        if (_alertOpen)
        {
            // Capped: a machine that keeps failing would otherwise build a queue of
            // identical cards to tap through, and the fault behind the fourth one is
            // the same fault. The badge and the log keep the full picture.
            if (_pendingAlerts.Count < MaxQueuedAlerts)
                _pendingAlerts.Enqueue(notification);
            return;
        }

        _alertOpen = true;
        AlertIcon.Data = Geometry.Parse(notification.IconData);
        AlertTitle.Text = notification.Title;
        AlertMessage.Text = notification.Message;

        AlertScrim.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            AlertScrim.Opacity = 1;
            AlertCard.Classes.Add("shown");
        }, DispatcherPriority.Background);
    }

    private void OnAlertOkClick(object? sender, RoutedEventArgs e)
    {
        AlertCard.Classes.Remove("shown");
        AlertScrim.Opacity = 0;

        DispatcherTimer.RunOnce(() =>
        {
            AlertScrim.IsVisible = false;
            _alertOpen = false;
            if (_pendingAlerts.Count > 0)
                ShowAlert(_pendingAlerts.Dequeue());
        }, TimeSpan.FromMilliseconds(260));
    }

}

/// <summary>
/// One banner on screen. Declared at namespace level because compiled bindings need a
/// type the XAML can name, and it carries its own position so the slide in and out is a
/// property change rather than something the layer has to animate by hand.
/// </summary>
public sealed class ToastItem : INotifyPropertyChanged
{
    private Thickness _offset;
    private double _opacity;

    public ToastItem(MachineNotification notification)
    {
        Title = notification.Title;
        Message = notification.Message;
        IconData = notification.IconData;
        Accent = notification.Accent;
    }

    public string Title { get; }
    public string Message { get; }
    public string IconData { get; }
    public IBrush Accent { get; }

    /// <summary>Set while the exit animation runs, so a tap during it is ignored.</summary>
    public bool IsLeaving { get; set; }

    public DispatcherTimer? Timer { get; set; }

    public Thickness Offset
    {
        get => _offset;
        set
        {
            if (_offset == value) return;
            _offset = value;
            OnPropertyChanged(nameof(Offset));
        }
    }

    public double Opacity
    {
        get => _opacity;
        set
        {
            if (Math.Abs(_opacity - value) < 0.001) return;
            _opacity = value;
            OnPropertyChanged(nameof(Opacity));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
