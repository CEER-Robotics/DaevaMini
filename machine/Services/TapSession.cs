using System;
using System.Threading;
using System.Threading.Tasks;
using DaevaMini.Models;

namespace DaevaMini.Services;

/// <summary>
/// One beer being drawn. Holds a normally-closed valve open by telling the board, over
/// and over, that this application is still running.
/// </summary>
/// <remarks>
/// The board deliberately refuses to hold the valve open on a single command: each TAP
/// buys only a couple of seconds. That is what makes a crash safe — if this process
/// dies, or the cable is pulled, nothing renews the lease and the valve shuts by
/// itself. The cost is that somebody has to keep sending, which is this class.
/// </remarks>
public sealed class TapSession : IAsyncDisposable
{
    // Comfortably inside the board's keep-alive window, so one dropped line on a
    // noisy link does not close the valve under the guest's glass.
    private const int KeepAliveIntervalMs = 900;

    private readonly string _keepAliveCommand;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    private TapSession(string keepAliveCommand)
    {
        _keepAliveCommand = keepAliveCommand;
        _loop = Task.Run(KeepAliveLoop);
    }

    /// <summary>
    /// Opens the valve for a drink, or returns null when the board will not start:
    /// not connected, no channel for the ingredient, or busy with something else.
    /// </summary>
    public static TapSession? TryOpen(Cocktail cocktail, int channel)
    {
        var manager = ArduinoSerialManager.Instance;
        if (!manager.IsConnected)
        {
            Console.WriteLine("[TapSession] Arduino not connected, refusing to open the tap");
            return null;
        }

        var rgb = cocktail.LedRgb ?? ((byte)255, (byte)200, (byte)40);
        string command = ArduinoProtocolHelper.BuildTapCommand(rgb, channel);
        Console.WriteLine($"[TapSession] Opening: {command}");

        if (!manager.Send(command))
        {
            Console.WriteLine("[TapSession] Failed to write the open command");
            return null;
        }

        return new TapSession(command);
    }

    private async Task KeepAliveLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(KeepAliveIntervalMs, _cts.Token);
                ArduinoSerialManager.Instance.Send(_keepAliveCommand);
            }
        }
        catch (OperationCanceledException)
        {
            // Closing normally.
        }
        catch (Exception ex)
        {
            // Never let this take the app down: stopping the loop is itself the safe
            // outcome, because the valve then closes on its own.
            Console.WriteLine($"[TapSession] Keep-alive stopped: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops renewing and closes the valve now. Even if the close command is lost, the
    /// valve shuts within the keep-alive window because nothing renews it any more.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _loop;
        }
        catch (Exception)
        {
            // Already logged in the loop.
        }
        _cts.Dispose();

        Console.WriteLine("[TapSession] Closing the tap");
        ArduinoSerialManager.Instance.Send(ArduinoProtocolHelper.TapOffCommand);
    }
}
