using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;

namespace DaevaMini.Services;

/// <summary>How a notification asks for attention.</summary>
public enum NotificationStyle
{
    /// <summary>
    /// Slides in over the top of whatever screen is open and leaves on its own, the way
    /// a phone shows a push. For things the bar should know about but can finish the
    /// drink in their hand first: a container running low or just run dry.
    /// </summary>
    Push,

    /// <summary>
    /// A card in the middle of the screen over a dimmed page, dismissed by hand - the
    /// shape iOS uses for "battery low". For a fault: something was asked of the machine
    /// and it did not happen, so it has to be read, not glimpsed.
    /// </summary>
    Alert
}

/// <summary>One thing the machine wants to say.</summary>
/// <param name="Key">
/// Identity for de-duplication. Two notifications with the same key inside the cooldown
/// are the same event said twice - a pour retried three times against a dead board must
/// not stack three alerts.
/// </param>
public sealed record MachineNotification(
    string Key,
    NotificationStyle Style,
    WarningLevel Level,
    string Title,
    string Message,
    string IconData)
{
    /// <summary>Red for something already broken, amber for something about to break.</summary>
    public IBrush Accent => Level == WarningLevel.Critical
        ? MachineWarning.CriticalBrush
        : MachineWarning.WarningBrush;
}

/// <summary>
/// The machine's single voice. Pages and the main window post here; the notification
/// layer on top of the window is the only thing that listens, so no screen has to carry
/// its own banner and a notification raised mid-pour is still seen on the next page.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="MachineWarningService"/>, which answers "what is
/// wrong right now" for the menu badge and keeps answering it for as long as it is true.
/// This one answers "what just happened", which is only worth interrupting someone for
/// once. The two are joined in <see cref="SeedOrDiffContainers"/>: a warning that was not
/// in the previous snapshot has just appeared, and that is the push.
/// </remarks>
public sealed class MachineNotificationService
{
    // Material icon paths, kept here so the notification carries its own glyph and the
    // layer stays a dumb renderer.
    private const string BottleIcon =
        "M7,2V4H8V6H7.5A1.5,1.5 0 0,0 6,7.5V21A1,1 0 0,0 7,22H17A1,1 0 0,0 18,21V7.5A1.5,1.5 0 0,0 16.5,6H16V4H17V2H7M10,4H14V6H10V4Z";
    private const string FaultIcon =
        "M13,14H11V9H13M13,18H11V16H13M1,21H23L12,2L1,21Z";

    /// <summary>
    /// How long the same key stays silent. Long enough that a stuck condition reporting
    /// itself on every pour does not become a wall of banners, short enough that a real
    /// second occurrence ten minutes later is still announced.
    /// </summary>
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    private static MachineNotificationService? _instance;
    private static readonly object LockObject = new();

    private readonly Dictionary<string, DateTime> _lastPosted = new(StringComparer.Ordinal);

    /// <summary>
    /// Titles of the container warnings as of the last refresh. Null until the first one:
    /// the standing state at startup is the menu badge's job, and announcing a bottle that
    /// was already empty when the machine was switched on would be noise.
    /// </summary>
    private HashSet<string>? _knownContainerWarnings;

    private bool _attached;

    public static MachineNotificationService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (LockObject)
                {
                    _instance ??= new MachineNotificationService();
                }
            }
            return _instance;
        }
    }

    private MachineNotificationService()
    {
    }

    /// <summary>Raised on the UI thread for every notification that survives de-duplication.</summary>
    public event EventHandler<MachineNotification>? Posted;

    /// <summary>
    /// Starts watching the warning service for containers that have just dropped into
    /// the red. Called once, by the main window, so the stream is alive on every screen.
    /// </summary>
    public void Attach()
    {
        // Idempotent: both main windows may call it and a second subscription would
        // double every push.
        if (_attached) return;
        _attached = true;
        MachineWarningService.Instance.WarningsChanged += (_, _) => SeedOrDiffContainers();
    }

    // ------------------------------------------------------------------ posting

    /// <summary>A fault: the machine was asked to do something and could not.</summary>
    public void PostFault(string key, string title, string message)
        => Post(new MachineNotification(
            key, NotificationStyle.Alert, WarningLevel.Critical, title, message, FaultIcon));

    public void Post(MachineNotification notification)
    {
        lock (_lastPosted)
        {
            var now = DateTime.UtcNow;
            if (_lastPosted.TryGetValue(notification.Key, out var last) && now - last < Cooldown)
                return;
            _lastPosted[notification.Key] = now;
        }

        // Posted rather than invoked inline: callers include the serial code running off
        // the UI thread, and the layer that listens builds controls.
        Dispatcher.UIThread.Post(() => Posted?.Invoke(this, notification));
    }

    /// <summary>
    /// Clears the cooldown for a key so the next occurrence is announced immediately.
    /// Used when the condition is known to have been dealt with - a refilled container
    /// that runs dry again is new news, not a repeat.
    /// </summary>
    public void Forget(string key)
    {
        lock (_lastPosted)
            _lastPosted.Remove(key);
    }

    // ------------------------------------------------------- containers running out

    private void SeedOrDiffContainers()
    {
        var current = MachineWarningService.Instance.Warnings
            .Where(w => w.Kind == MachineWarningKind.ContainerLevel)
            .ToList();

        var titles = current.Select(w => w.Title).ToHashSet(StringComparer.Ordinal);

        if (_knownContainerWarnings == null)
        {
            _knownContainerWarnings = titles;
            return;
        }

        // Gone means refilled: let it speak again next time rather than sitting out the
        // cooldown on a container that has since been changed.
        foreach (string cleared in _knownContainerWarnings.Except(titles))
            Forget(ContainerKey(cleared));

        foreach (var warning in current)
        {
            if (_knownContainerWarnings.Contains(warning.Title))
                continue;

            Post(new MachineNotification(
                ContainerKey(warning.Title),
                NotificationStyle.Push,
                warning.Level,
                warning.Level == WarningLevel.Critical
                    ? "Cambio contenitore necessario"
                    : "Contenitore in esaurimento",
                $"{warning.Title}. {warning.Detail}",
                BottleIcon));
        }

        _knownContainerWarnings = titles;
    }

    private static string ContainerKey(string warningTitle) => "container:" + warningTitle;
}
