using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Runtime.CompilerServices;

namespace DaevaMini.Controls;

/// <summary>
/// Makes a <see cref="ScrollViewer"/> scroll by dragging it, the way a phone does,
/// with a flick carrying on after the finger leaves.
/// </summary>
/// <remarks>
/// The machine is driven by a finger on a panel, and its lists were only scrollable by
/// the scrollbar: a hairline the Fluent theme only widens on pointer hover, which never
/// happens without a mouse. Dragging is the gesture people actually try.
///
/// This works on pointer events rather than touch gestures on purpose. The panel's
/// events arrive as ordinary pointer input, and the swipe already in the cocktail menu
/// is built the same way, so mouse and finger behave identically here.
///
/// Usage: <c>controls:DragScroll.Enabled="True"</c> on any ScrollViewer.
/// </remarks>
public static class DragScroll
{
    /// <summary>
    /// How far the pointer must travel before a press counts as a drag rather than a
    /// tap. Without it, the small movement of a finger pressing a list button would
    /// scroll the list and swallow the tap.
    /// </summary>
    private const double DragThresholdPx = 10;

    /// <summary>Velocity kept per frame after release: 0.9 is a short, controlled glide.</summary>
    private const double Friction = 0.9;

    /// <summary>Below this (pixels per millisecond) the glide is over.</summary>
    private const double MinVelocity = 0.02;

    private const double FrameMs = 16;

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("Enabled", typeof(DragScroll));

    public static void SetEnabled(ScrollViewer target, bool value) => target.SetValue(EnabledProperty, value);

    public static bool GetEnabled(ScrollViewer target) => target.GetValue(EnabledProperty);

    private sealed class State
    {
        public bool Pressed;
        public bool Dragging;
        public Point Origin;
        public double OriginOffsetY;
        public Point LastPoint;
        public DateTime LastMoveAt;
        public double VelocityY;
        public DispatcherTimer? Glide;
    }

    private static readonly ConditionalWeakTable<ScrollViewer, State> States = new();

    static DragScroll()
    {
        EnabledProperty.Changed.AddClassHandler<ScrollViewer>((viewer, args) =>
        {
            if (args.NewValue is true)
                Attach(viewer);
            else
                Detach(viewer);
        });
    }

    private static void Attach(ScrollViewer viewer)
    {
        // Tunnelling, and taking handled events too, so the buttons inside the list
        // cannot consume the press before the drag is recognised.
        viewer.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        viewer.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        viewer.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        viewer.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static void Detach(ScrollViewer viewer)
    {
        viewer.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        viewer.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
        viewer.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        viewer.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        StopGlide(viewer);
        States.Remove(viewer);
    }

    private static State StateOf(ScrollViewer viewer) => States.GetValue(viewer, _ => new State());

    private static double MaxOffsetY(ScrollViewer viewer)
        => Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);

    private static void ScrollTo(ScrollViewer viewer, double y)
    {
        double clamped = Math.Clamp(y, 0, MaxOffsetY(viewer));
        viewer.Offset = new Vector(viewer.Offset.X, clamped);
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ScrollViewer viewer) return;

        // Touching the list during a glide should stop it, the way it does on a phone.
        StopGlide(viewer);

        var state = StateOf(viewer);
        state.Pressed = true;
        state.Dragging = false;
        state.Origin = e.GetPosition(viewer);
        state.OriginOffsetY = viewer.Offset.Y;
        state.LastPoint = state.Origin;
        state.LastMoveAt = DateTime.UtcNow;
        state.VelocityY = 0;
    }

    private static void OnMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not ScrollViewer viewer) return;

        var state = StateOf(viewer);
        if (!state.Pressed) return;

        Point current = e.GetPosition(viewer);
        double travelled = state.Origin.Y - current.Y;

        if (!state.Dragging)
        {
            if (Math.Abs(travelled) < DragThresholdPx) return;
            if (MaxOffsetY(viewer) <= 0) return;  // nothing to scroll

            state.Dragging = true;
            // Capturing takes the pointer away from the button under the finger, so a
            // drag that started on a list item does not end up pressing it.
            e.Pointer.Capture(viewer);
        }

        var now = DateTime.UtcNow;
        double elapsedMs = (now - state.LastMoveAt).TotalMilliseconds;
        if (elapsedMs > 0)
            state.VelocityY = (state.LastPoint.Y - current.Y) / elapsedMs;

        state.LastPoint = current;
        state.LastMoveAt = now;

        ScrollTo(viewer, state.OriginOffsetY + travelled);
        e.Handled = true;
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not ScrollViewer viewer) return;

        var state = StateOf(viewer);
        bool wasDragging = state.Dragging;
        state.Pressed = false;
        state.Dragging = false;

        if (!wasDragging) return;

        e.Pointer.Capture(null);
        // Swallow the release so the button the drag started on does not fire.
        e.Handled = true;

        // A finger resting still before lifting means "stop here", not "throw".
        if ((DateTime.UtcNow - state.LastMoveAt).TotalMilliseconds > 80)
            return;

        StartGlide(viewer, state);
    }

    private static void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sender is not ScrollViewer viewer) return;
        var state = StateOf(viewer);
        state.Pressed = false;
        state.Dragging = false;
    }

    private static void StartGlide(ScrollViewer viewer, State state)
    {
        if (Math.Abs(state.VelocityY) < MinVelocity) return;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FrameMs) };
        state.Glide = timer;

        timer.Tick += (_, _) =>
        {
            state.VelocityY *= Friction;

            double target = viewer.Offset.Y + state.VelocityY * FrameMs;
            double max = MaxOffsetY(viewer);

            // Stop at the ends rather than grinding against them.
            if (Math.Abs(state.VelocityY) < MinVelocity || target <= 0 || target >= max)
            {
                ScrollTo(viewer, target);
                StopGlide(viewer);
                return;
            }

            ScrollTo(viewer, target);
        };

        timer.Start();
    }

    private static void StopGlide(ScrollViewer viewer)
    {
        if (!States.TryGetValue(viewer, out var state)) return;
        state.Glide?.Stop();
        state.Glide = null;
        state.VelocityY = 0;
    }
}
