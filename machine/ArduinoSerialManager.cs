using System;
using System.IO.Ports;
using System.Linq;

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

    private const string ForcedPort = null; // set to null to use auto-detect

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
                    Console.WriteLine($"[ArduinoManager] Forced port {ForcedPort} not available: {ex.Message}");
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
