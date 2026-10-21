using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DaevaMini.Services;

/// <summary>One access point found by a scan.</summary>
public sealed record WifiNetwork(string Ssid, int SignalPercent, bool Secured);

/// <summary>Outcome of a connection attempt, distinguishing "wrong password" from every
/// other failure so the UI can ask again instead of just saying "didn't work".</summary>
public enum WifiConnectResult
{
    Success,
    WrongPassword,
    NotFound,
    Failed,
}

/// <summary>Current radio state, as much as nmcli is willing to say quickly.</summary>
/// <param name="OtherSsid">What the machine's other radio is connected to, when it is on a
/// different network from the one this page manages.</param>
public sealed record WifiStatus(bool Connected, string? Ssid, string? IpAddress, string? OtherSsid = null);

/// <summary>
/// Wraps NetworkManager's <c>nmcli</c>, which is what Raspberry Pi OS (Bookworm and
/// later) uses for networking out of the box - this is the machine's actual target,
/// per the root CLAUDE.md. There is no library dependency to add: nmcli ships with the
/// OS image, and shelling out to it is the same thing raspi-config itself does.
/// </summary>
/// <remarks>
/// The dev box this is written and clicked through on is Windows, where nmcli does not
/// exist at all. Rather than make every screen unusable off the Pi, a missing nmcli
/// binary is treated the same way a missing Arduino already is elsewhere in this app
/// (see machine/CLAUDE.md, "simulates the dispense"): <see cref="IsSimulated"/> flips on
/// the first failed attempt to launch nmcli, and every call below then answers from a
/// small canned network list instead of failing outright.
/// </remarks>
public static class WifiService
{
    private const string Nmcli = "nmcli";

    /// <summary>
    /// True once nmcli has been found missing. Sticky for the process lifetime: if it is
    /// not on PATH once, it will not appear later, and re-probing on every call would
    /// just repeat the same failed process launch for nothing.
    /// </summary>
    public static bool IsSimulated { get; private set; }

    private static readonly WifiNetwork[] SimulatedNetworks =
    {
        new("Bar Wifi", 88, true),
        new("Ospiti", 54, false),
        new("TIM-12345678", 40, true),
    };

    /// <summary>Only meaningful in <see cref="IsSimulated"/> mode: what a simulated
    /// connect last succeeded to, so the status line has something to report.</summary>
    private static string? _simulatedSsid;

    /// <summary>
    /// The wireless interface every command is aimed at, or null to let nmcli choose.
    /// </summary>
    /// <remarks>
    /// The assembled Max has two radios: the Pi's built-in one, sealed inside the
    /// machine's metal body, and a USB adapter whose antenna hangs outside it. The
    /// difference is not subtle - measured on the bench, the same request to the backend
    /// took 0.2 s over the external adapter and 10 s over the internal radio, and the
    /// internal one can see exactly one access point where the external sees seven. Left
    /// to itself nmcli picks whichever device it likes, so setting up a new bar's network
    /// could silently land it on the radio that cannot reach anything.
    ///
    /// The external adapter is found by asking which wireless interface hangs off USB,
    /// rather than by assuming a name: interface numbering is not guaranteed. Set
    /// DAEVA_WIFI_INTERFACE to override. On a machine with only the built-in radio - the
    /// Mini, or a dev box - nothing is found and nmcli is left to decide, exactly as
    /// before.
    ///
    /// The wireless devices come from nmcli and their bus path from <c>readlink -f</c>,
    /// not from walking /sys with .NET's directory APIs. The first version did the latter,
    /// and on the Pi it never found the USB radio (/sys/class/net is all symlinks), so
    /// every command silently went to whichever radio NetworkManager preferred - seen in
    /// a trace of the calls the page made, none of which carried an ifname.
    /// </remarks>
    private static async Task<string?> PreferredInterfaceAsync()
    {
        string? forced = Environment.GetEnvironmentVariable("DAEVA_WIFI_INTERFACE");
        if (!string.IsNullOrWhiteSpace(forced))
            return forced.Trim();

        if (_preferredResolved)
            return _preferred;

        try
        {
            var devices = await RunAsync(Nmcli, new[] { "-t", "-f", "DEVICE,TYPE", "dev" }, timeoutMs: 5000);
            if (!devices.Found || devices.ExitCode != 0)
                return null;

            foreach (string line in devices.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = SplitTerse(line);
                if (fields.Count < 2 || fields[1] != "wifi")
                    continue;

                // The interface's real place in the bus tree: a USB radio sits under a usb
                // controller, the built-in one under the SDIO/mmc host.
                var path = await RunAsync("readlink", new[] { "-f", $"/sys/class/net/{fields[0]}" }, timeoutMs: 3000);
                if (path is { Found: true, ExitCode: 0 } && path.StdOut.Contains("/usb", StringComparison.Ordinal))
                {
                    _preferred = fields[0];
                    break;
                }
            }

            // Remembered either way: the radios do not change while the app runs, and a
            // machine with no USB radio should not pay for this on every call.
            _preferredResolved = true;
            Console.WriteLine($"[WifiService] Radio used for Wi-Fi setup: {_preferred ?? "(NetworkManager decides)"}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WifiService] Could not work out which radio to use: {ex.Message}");
        }

        return _preferred;
    }

    private static string? _preferred;
    private static bool _preferredResolved;

    /// <summary>Appends <c>ifname &lt;dev&gt;</c> when a radio has been chosen.</summary>
    private static async Task<string[]> WithInterfaceAsync(params string[] args)
    {
        string? device = await PreferredInterfaceAsync();
        if (string.IsNullOrEmpty(device))
            return args;

        var withIfname = new List<string>(args) { "ifname", device };
        return withIfname.ToArray();
    }

    /// <summary>
    /// Networks in range. <paramref name="rescan"/> false answers at once from what
    /// NetworkManager already saw on its own background scans; true asks the radio to
    /// scan now, which takes several seconds. The page shows the first and then refines
    /// it with the second, so the list is never empty while the radio works.
    /// </summary>
    public static async Task<IReadOnlyList<WifiNetwork>> ScanAsync(bool rescan = true)
    {
        var result = await RunAsync(Nmcli, await WithInterfaceAsync(
            "-t", "-f", "SSID,SIGNAL,SECURITY", "dev", "wifi", "list", "--rescan", rescan ? "yes" : "no"),
            timeoutMs: 20000);
        if (!result.Found)
            return SimulatedNetworks;

        if (result.ExitCode != 0)
            return Array.Empty<WifiNetwork>();

        // Repeated SSIDs (several access points on the same network) collapse to
        // whichever one currently answers loudest.
        var byName = new Dictionary<string, WifiNetwork>(StringComparer.Ordinal);
        foreach (string line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = SplitTerse(line);
            string ssid = fields.Count > 0 ? fields[0] : string.Empty;
            if (string.IsNullOrWhiteSpace(ssid))
                continue; // hidden network broadcasting no name: nothing to show or tap

            int signal = fields.Count > 1 && int.TryParse(fields[1], out int s) ? s : 0;
            bool secured = fields.Count > 2 && !string.IsNullOrWhiteSpace(fields[2]);

            if (!byName.TryGetValue(ssid, out var existing) || signal > existing.SignalPercent)
                byName[ssid] = new WifiNetwork(ssid, signal, secured);
        }

        return byName.Values.OrderByDescending(n => n.SignalPercent).ToList();
    }

    /// <summary>Empty password for an open network; nmcli itself decides whether one was needed.</summary>
    public static async Task<WifiConnectResult> ConnectAsync(string ssid, string password)
    {
        var result = string.IsNullOrEmpty(password)
            ? await RunAsync(Nmcli, await WithInterfaceAsync("dev", "wifi", "connect", ssid), timeoutMs: 30000)
            : await RunAsync(Nmcli, await WithInterfaceAsync("dev", "wifi", "connect", ssid, "password", password), timeoutMs: 30000);

        if (!result.Found)
        {
            // Kept simple on purpose: a short-enough password (or an open network) reads
            // as wrong, everything else succeeds. Good enough to click through the page.
            await Task.Delay(1200);
            if (!string.IsNullOrEmpty(password) && password.Length < 8)
                return WifiConnectResult.WrongPassword;

            _simulatedSsid = ssid;
            return WifiConnectResult.Success;
        }

        if (result.ExitCode == 0)
            return WifiConnectResult.Success;

        if (result.StdErr.Contains("Secrets were required", StringComparison.OrdinalIgnoreCase)
            || result.StdErr.Contains("802-11-wireless-security", StringComparison.OrdinalIgnoreCase))
            return WifiConnectResult.WrongPassword;

        if (result.StdErr.Contains("No network with SSID", StringComparison.OrdinalIgnoreCase))
            return WifiConnectResult.NotFound;

        return WifiConnectResult.Failed;
    }

    /// <summary>
    /// What each radio is connected to, from NetworkManager's cache - instant, no scan.
    /// </summary>
    /// <remarks>
    /// The Max has two radios and both can be connected at once (it was found on A
    /// through the internal one and on the bar's router through the external one). The
    /// status names the radio this page manages - the external one when there is one -
    /// and mentions the other, so "connected to X" is never a coin toss between them.
    ///
    /// Two earlier mistakes are avoided on purpose: "dev wifi" without "--rescan no"
    /// triggered a scan and took ~4 s before the page showed anything, and the address
    /// was looked up by SSID as if it were the connection's name, which it often is not
    /// (the router's profile here is called "FRITZ-wlan1"), so the IP never appeared.
    /// </remarks>
    public static async Task<WifiStatus> GetStatusAsync()
    {
        var probe = await RunAsync(Nmcli,
            new[] { "-t", "-f", "ACTIVE,SSID,DEVICE", "dev", "wifi", "list", "--rescan", "no" }, timeoutMs: 8000);
        if (!probe.Found)
        {
            return _simulatedSsid == null
                ? new WifiStatus(false, null, null)
                : new WifiStatus(true, _simulatedSsid, "192.168.1.42");
        }

        if (probe.ExitCode != 0)
            return new WifiStatus(false, null, null);

        // Device -> SSID for every radio that is connected right now.
        var active = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in probe.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = SplitTerse(line);
            if (f.Count >= 3 && f[0].Equals("yes", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(f[1]))
                active.TryAdd(f[2], f[1]);
        }

        if (active.Count == 0)
            return new WifiStatus(false, null, null);

        string? preferred = await PreferredInterfaceAsync();
        string device = preferred != null && active.ContainsKey(preferred) ? preferred : active.Keys.First();
        string ssid = active[device];

        string? other = active
            .Where(kv => kv.Key != device && kv.Value != ssid)
            .Select(kv => kv.Value)
            .FirstOrDefault();

        // Asked of the device, not of a connection named after the SSID.
        var ipResult = await RunAsync(Nmcli, new[] { "-g", "IP4.ADDRESS", "dev", "show", device }, timeoutMs: 5000);
        string? ip = ipResult is { Found: true, ExitCode: 0 }
            ? ipResult.StdOut.Split(new[] { '\n', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Split('/')[0].Trim()
            : null;

        return new WifiStatus(true, ssid, string.IsNullOrWhiteSpace(ip) ? null : ip, other);
    }

    /// <summary>
    /// Splits one nmcli terse (<c>-t</c>) line on ':', honouring its own escaping: a
    /// literal colon or backslash inside a field arrives as <c>\:</c> / <c>\\</c>.
    /// </summary>
    private static List<string> SplitTerse(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length)
            {
                current.Append(line[i + 1]);
                i++;
            }
            else if (c == ':')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private readonly record struct RunResult(bool Found, int ExitCode, string StdOut, string StdErr);

    /// <summary>
    /// Runs one command and waits for it, tolerating both a missing executable (sets
    /// <see cref="IsSimulated"/>) and a hang (killed after <paramref name="timeoutMs"/>).
    /// Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>, never a single
    /// shell string, so an SSID or password containing quotes or spaces cannot break out
    /// of its argument - there is no shell here to break out of.
    /// </summary>
    private static async Task<RunResult> RunAsync(string fileName, params string[] args)
        => await RunAsync(fileName, args, timeoutMs: 15000);

    private static async Task<RunResult> RunAsync(string fileName, IReadOnlyList<string> args, int timeoutMs)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            IsSimulated = true;
            return new RunResult(false, -1, string.Empty, string.Empty);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return new RunResult(true, -1, string.Empty, "timeout");
        }

        return new RunResult(true, process.ExitCode, await stdoutTask, await stderrTask);
    }
}
