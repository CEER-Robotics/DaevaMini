using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
public sealed record WifiStatus(bool Connected, string? Ssid, string? IpAddress);

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

    public static async Task<IReadOnlyList<WifiNetwork>> ScanAsync()
    {
        var result = await RunAsync(Nmcli, "-t", "-f", "SSID,SIGNAL,SECURITY", "dev", "wifi", "list", "--rescan", "yes");
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
            ? await RunAsync(Nmcli, "dev", "wifi", "connect", ssid)
            : await RunAsync(Nmcli, new[] { "dev", "wifi", "connect", ssid, "password", password }, timeoutMs: 30000);

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

    public static async Task<WifiStatus> GetStatusAsync()
    {
        var probe = await RunAsync(Nmcli, "-t", "-f", "active,ssid", "dev", "wifi");
        if (!probe.Found)
        {
            return _simulatedSsid == null
                ? new WifiStatus(false, null, null)
                : new WifiStatus(true, _simulatedSsid, "192.168.1.42");
        }

        if (probe.ExitCode != 0)
            return new WifiStatus(false, null, null);

        string? ssid = probe.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(SplitTerse)
            .FirstOrDefault(f => f.Count >= 2 && f[0].Equals("yes", StringComparison.OrdinalIgnoreCase))
            ?.ElementAtOrDefault(1);

        if (ssid == null)
            return new WifiStatus(false, null, null);

        var ipResult = await RunAsync(Nmcli, "-g", "IP4.ADDRESS", "connection", "show", ssid);
        string? ip = ipResult is { Found: true, ExitCode: 0 }
            ? ipResult.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Split('/')[0].Trim()
            : null;

        return new WifiStatus(true, ssid, string.IsNullOrWhiteSpace(ip) ? null : ip);
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
