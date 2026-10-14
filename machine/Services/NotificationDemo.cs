#if DEBUG
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DaevaMini.Services;

/// <summary>
/// Posts made-up notifications on a loop so the two styles can be looked at without
/// emptying a bottle or unplugging the board. Started only by <c>--DemoNotifiche</c> on
/// the command line, and compiled out of Release entirely: a machine behind a bar must
/// never invent a fault.
/// </summary>
internal static class NotificationDemo
{
    private const string CommandLineFlag = "--DemoNotifiche";

    /// <summary>Gap between notifications. Long enough to watch one arrive and leave.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(9);

    /// <summary>Waits for the splash to be up before the first one lands.</summary>
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(4);

    public static bool IsRequested =>
        Array.Exists(Environment.GetCommandLineArgs(),
            a => string.Equals(a, CommandLineFlag, StringComparison.OrdinalIgnoreCase));

    public static void StartIfRequested()
    {
        if (!IsRequested) return;

        Console.WriteLine($"[NotificationDemo] {CommandLineFlag} on: posting a notification every {Interval.TotalSeconds:0}s");
        _ = Task.Run(RunAsync);
    }

    private static async Task RunAsync()
    {
        await Task.Delay(StartDelay);

        // Two banners then an alert, so the pair can be compared side by side before the
        // modal one covers the screen.
        var script = new Action<int>[]
        {
            round => MachineNotificationService.Instance.Post(new MachineNotification(
                $"demo:empty:{round}", NotificationStyle.Push, WarningLevel.Critical,
                "Cambio contenitore necessario",
                "Gin esaurita. Linea 3: sostituire il contenitore.",
                BottleIcon)),

            round => MachineNotificationService.Instance.Post(new MachineNotification(
                $"demo:low:{round}", NotificationStyle.Push, WarningLevel.Warning,
                "Contenitore in esaurimento",
                "Tonica in esaurimento. Linea 7: restano circa 180 ml su 1500.",
                BottleIcon)),

            round => MachineNotificationService.Instance.PostFault(
                $"demo:fault:{round}",
                "Macchina non collegata",
                "La scheda non risponde sulla porta seriale. Controlla il cavo e riavvia la macchina."),

            round => MachineNotificationService.Instance.Post(new MachineNotification(
                $"demo:empty2:{round}", NotificationStyle.Push, WarningLevel.Critical,
                "Cambio contenitore necessario",
                "Prosecco esaurito. Linea 11: sostituire il fusto.",
                BottleIcon)),

            round => MachineNotificationService.Instance.PostFault(
                $"demo:tap:{round}",
                "Spina non apribile",
                "La macchina non ha aperto la spina. Controlla che il fusto sia collegato e attivo.")
        };

        for (int step = 0; ; step++)
        {
            // The key carries the round number so the two-minute de-duplication, which is
            // the right behaviour for real events, does not silence the demo.
            script[step % script.Length](step / script.Length);
            await Task.Delay(Interval);
        }
    }

    private const string BottleIcon =
        "M7,2V4H8V6H7.5A1.5,1.5 0 0,0 6,7.5V21A1,1 0 0,0 7,22H17A1,1 0 0,0 18,21V7.5A1.5,1.5 0 0,0 16.5,6H16V4H17V2H7M10,4H14V6H10V4Z";
}
#endif
