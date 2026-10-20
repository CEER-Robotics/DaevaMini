using System;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;

namespace DaevaMini;

public sealed class ArduinoSerialManager : IDisposable
{
    private static ArduinoSerialManager? _instance;
    private static readonly object _lockObject = new();
    private ArduinoSerial? _serial;
    private bool _disposed = false;

    public static ArduinoSerialManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lockObject)
                {
                    if (_instance == null)
                    {
                        _instance = new ArduinoSerialManager();
                    }
                }
            }
            return _instance;
        }
    }

    private ArduinoSerialManager()
    {
        // Private constructor for singleton
    }

    public bool IsConnected => _serial?.IsConnected == true;

    public void Initialize()
    {
        if (_disposed)
            return;

        lock (_lockObject)
        {
            if (_serial != null)
            {
                Console.WriteLine("[ArduinoManager] Already initialized");
                return;
            }

            Console.WriteLine("[ArduinoManager] Initializing Arduino connection...");
            string? portName = FindArduinoPort();
            
            if (portName != null)
            {
                Console.WriteLine($"[ArduinoManager] Found Arduino on port: {portName}");
                try
                {
                    _serial = new ArduinoSerial(portName);
                    Console.WriteLine($"[ArduinoManager] Successfully connected to Arduino on port: {portName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ArduinoManager] Failed to connect to Arduino on port {portName}: {ex.Message}");
                    _serial = null;
                }
            }
            else
            {
                Console.WriteLine("[ArduinoManager] Could not find Arduino port - no serial connection available");
            }
        }
    }

    public bool Send(string command)
    {
        if (_disposed || _serial == null)
            return false;

        return _serial.Send(command);
    }

    public string? ReadLine()
    {
        if (_disposed || _serial == null)
            return null;

        return _serial.ReadLine();
    }

    /// <summary>
    /// Sends an ACTIVE command and waits for the firmware to acknowledge it, retrying
    /// when the command is lost on the wire. Returns false only if the write itself
    /// failed; otherwise <paramref name="response"/> carries the firmware's answer.
    /// </summary>
    /// <remarks>
    /// The board loses inbound bytes while its LED driver has interrupts disabled, and
    /// answers nothing at all for a line it cannot parse, so a dropped command is
    /// indistinguishable from silence and has to be repeated. Retrying cannot pour
    /// twice: once the firmware is dispensing it is no longer in WAIT, so it replies
    /// IGNORED ACTIVE and returns before scheduling any pump.
    /// </remarks>
    public bool TrySendActive(string command, out ActivateResponse response, int attempts = 3)
    {
        response = ActivateResponse.NoResponse;

        ArduinoSerial? serial = _serial;
        if (_disposed || serial == null)
            return false;

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            if (!serial.Exchange(command, MaxAckLines, IsActivateAnswer, out string? line))
                return false;

            response = ArduinoProtocolHelper.ParseActivateResponse(line);

            // A resend that comes back IGNORED means the board is no longer in WAIT —
            // i.e. it is already dispensing the command we just repeated, so the lost
            // one did land. Reporting that as a failure would show an error on screen
            // while the pour is running.
            if (response == ActivateResponse.Ignored && attempt > 1)
            {
                Console.WriteLine("[ArduinoManager] Resend reported IGNORED: the earlier ACTIVE was accepted, treating as success");
                response = ActivateResponse.Success;
                return true;
            }

            if (response != ActivateResponse.NoResponse)
                return true;

            if (attempt < attempts)
                Console.WriteLine($"[ArduinoManager] No answer to ACTIVE (attempt {attempt}/{attempts}), resending");
        }

        Console.WriteLine($"[ArduinoManager] ACTIVE unanswered after {attempts} attempts");
        return true;
    }

    /// <summary>
    /// <see cref="TrySendActive"/> off the calling thread. Every UI caller should use this:
    /// one exchange blocks for as long as the read timeout, and three attempts against a
    /// board that has stopped answering add up to seconds of a frozen touchscreen.
    /// </summary>
    public Task<(bool Sent, ActivateResponse Response)> TrySendActiveAsync(string command, int attempts = 3)
        => Task.Run(() =>
        {
            bool sent = TrySendActive(command, out ActivateResponse response, attempts);
            return (sent, response);
        });

    /// <summary>
    /// Sends a TAP (open or keep-alive) and reports what the board answered, so a refused
    /// open is not mistaken for a valve that is now pouring.
    /// </summary>
    public bool TrySendTap(string command, out TapResponse response)
    {
        response = TapResponse.NoResponse;

        ArduinoSerial? serial = _serial;
        if (_disposed || serial == null)
            return false;

        if (!serial.Exchange(command, MaxAckLines, IsTapAnswer, out string? line))
            return false;

        response = ArduinoProtocolHelper.ParseTapResponse(line);
        return true;
    }

    /// <summary><see cref="TrySendTap"/> off the calling thread.</summary>
    public Task<(bool Sent, TapResponse Response)> TrySendTapAsync(string command)
        => Task.Run(() =>
        {
            bool sent = TrySendTap(command, out TapResponse response);
            return (sent, response);
        });

    /// <summary>Fire-and-forget write on a worker thread, for commands whose reply nobody reads.</summary>
    public Task SendAsync(string command) => Task.Run(() => Send(command));

    /// How many unsolicited lines (DONE, OK READY) an exchange will step over before
    /// giving up on finding the acknowledgement it is waiting for.
    private const int MaxAckLines = 4;

    private static bool IsActivateAnswer(string line)
        => ArduinoProtocolHelper.ParseActivateResponse(line) != ActivateResponse.NoResponse;

    private static bool IsTapAnswer(string line)
        => ArduinoProtocolHelper.ParseTapResponse(line) != TapResponse.NoResponse;

    // Explicit port override, e.g. DAEVA_SERIAL_PORT=/dev/ttyAMA0 when the board
    // is wired to the Pi's GPIO 14/15 UART instead of USB. Auto-detection cannot
    // resolve that case on its own: it prefers ttyUSB/ttyACM/COM names, and the
    // fallback takes whichever port the OS happens to list first — on a Pi 5
    // that is as likely to be the debug UART as the board. Unset = auto-detect.
    private static readonly string? ForcedPort =
        Environment.GetEnvironmentVariable("DAEVA_SERIAL_PORT");

    private static string? FindArduinoPort()
    {
        try
        {
            if (!string.IsNullOrEmpty(ForcedPort))
            {
                try
                {
                    using var testPort = new SerialPort(ForcedPort, 115200);
                    testPort.Open();
                    testPort.Close();
                    Console.WriteLine($"[ArduinoManager] Using forced port: {ForcedPort}");
                    return ForcedPort;
                }
                catch (Exception ex)
                {
                    // A named port is an instruction, not a hint: fail loudly rather than
                    // fall through to auto-detection. On the assembled Max the pump
                    // controller is on /dev/ttyAMA0 and the only other port on the Pi is
                    // the console UART, so guessing means opening the console, reporting
                    // "connected", and pouring nothing for as long as nobody looks. That
                    // happened: a reimaged SD card lost enable_uart/dtparam=uart0, ttyAMA0
                    // never appeared, and the machine ran for hours bound to ttyAMA10.
                    Console.WriteLine($"[ArduinoManager] Forced port {ForcedPort} not available: {ex.Message}");
                    Console.WriteLine("[ArduinoManager] DAEVA_SERIAL_PORT is set, so no other port will be tried. " +
                                      "Check that the port exists (ls -l /dev/ttyAMA*) and that nothing else holds it.");
                    return null;
                }
            }

            var ports = SerialPort.GetPortNames();
            
            Console.WriteLine($"[ArduinoManager] Scanning for serial ports... Found {ports.Length} port(s): {string.Join(", ", ports)}");
            
            // On Windows, typically COM ports (COM1, COM3, etc.)
            // On Linux, typically /dev/ttyUSB0, /dev/ttyACM0, etc.
            // On macOS, typically /dev/tty.usbserial, /dev/tty.usbmodem, etc.
            
            // Prefer USB serial devices
            var preferredPorts = ports.Where(p => 
                p.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("ACM", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            // On Windows, try lower COM numbers first (COM6 before COM11)
            if (preferredPorts.Any() && preferredPorts[0].StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                preferredPorts = preferredPorts.OrderBy(p =>
                {
                    var s = p.Substring(3);
                    return int.TryParse(s, out var n) ? n : int.MaxValue;
                }).ToList();
            }

            if (preferredPorts.Any())
            {
                Console.WriteLine($"[ArduinoManager] Found {preferredPorts.Count} preferred port(s): {string.Join(", ", preferredPorts)}");
                
                // Try to open the first preferred port to verify it's available
                foreach (var port in preferredPorts)
                {
                    try
                    {
                        Console.WriteLine($"[ArduinoManager] Testing port: {port}");
                        using var testPort = new SerialPort(port, 115200);
                        testPort.Open();
                        testPort.Close();
                        Console.WriteLine($"[ArduinoManager] Port {port} is available");
                        return port;
                    }
                    catch (Exception ex)
                    {
                        // Port is not available, try next
                        Console.WriteLine($"[ArduinoManager] Port {port} is not available: {ex.Message}");
                        continue;
                    }
                }
            }
            else
            {
                Console.WriteLine("[ArduinoManager] No preferred ports found");
            }

            // Fallback to first available port
            if (ports.Length > 0)
            {
                Console.WriteLine($"[ArduinoManager] Falling back to first available port: {ports[0]}");
                return ports[0];
            }
            
            Console.WriteLine("[ArduinoManager] No serial ports found on system");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ArduinoManager] Error during port detection: {ex.Message}");
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lockObject)
        {
            if (_disposed)
                return;

            _disposed = true;
            Console.WriteLine("[ArduinoManager] Disposing Arduino connection");
            _serial?.Dispose();
            _serial = null;
        }
    }
}
