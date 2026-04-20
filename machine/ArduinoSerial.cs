using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace DaevaMini;

public class ArduinoSerial : IDisposable
{
    private SerialPort? _port;
    private readonly string _portName;
    private readonly object _lockObject = new();
    private bool _disposed = false;
    private const int MaxRetryAttempts = 3;
    private const int RetryDelayMs = 1000;

    public bool IsConnected => _port?.IsOpen == true;

    public ArduinoSerial(string portName)
    {
        _portName = portName ?? throw new ArgumentNullException(nameof(portName));
        Connect();
    }

    private void Connect()
    {
        lock (_lockObject)
        {
            if (_disposed) return;

            try
            {
                // Close existing connection if any
                if (_port?.IsOpen == true)
                {
                    _port.Close();
                }
                _port?.Dispose();

                // Create new connection. DtrEnable/RtsEnable = false to avoid resetting
                // the board when the port is opened (Serial Monitor doesn't hold DTR/RTS by default).
                _port = new SerialPort(_portName, 115200)
                {
                    NewLine = "\n",
                    ReadTimeout = 2000,
                    WriteTimeout = 5000,
                    DtrEnable = false,
                    RtsEnable = false
                };

                _port.Open();
            }
            catch (Exception ex)
            {
                _port?.Dispose();
                _port = null;
                throw new InvalidOperationException($"Failed to connect to serial port {_portName}: {ex.Message}", ex);
            }
        }
    }

    private bool EnsureConnected()
    {
        if (_disposed) return false;

        lock (_lockObject)
        {
            if (_port?.IsOpen == true)
                return true;

            // Try to reconnect
            try
            {
                Connect();
                return _port?.IsOpen == true;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool Send(string command)
    {
        if (string.IsNullOrEmpty(command))
            return false;

        if (_disposed)
            return false;

        for (int attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            try
            {
                if (!EnsureConnected())
                {
                    if (attempt < MaxRetryAttempts - 1)
                    {
                        Thread.Sleep(RetryDelayMs);
                        continue;
                    }
                    return false;
                }

                lock (_lockObject)
                {
                    if (_port?.IsOpen == true)
                    {
                        _port.WriteLine(command);
                        return true;
                    }
                }
            }
            catch (TimeoutException ex)
            {
                Console.WriteLine($"[ArduinoSerial] Send timeout (attempt {attempt + 1}): {ex.Message}");
                if (attempt < MaxRetryAttempts - 1)
                {
                    Thread.Sleep(RetryDelayMs);
                    continue;
                }
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"[ArduinoSerial] Send invalid operation (attempt {attempt + 1}): {ex.Message}");
                if (attempt < MaxRetryAttempts - 1)
                {
                    Thread.Sleep(RetryDelayMs);
                    continue;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ArduinoSerial] Send failed (attempt {attempt + 1}): {ex.GetType().Name} - {ex.Message}");
                if (attempt < MaxRetryAttempts - 1)
                {
                    Thread.Sleep(RetryDelayMs);
                    try
                    {
                        Connect();
                    }
                    catch (Exception rex)
                    {
                        Console.WriteLine($"[ArduinoSerial] Reconnect failed: {rex.Message}");
                    }
                    continue;
                }
            }
        }

        return false;
    }

    public string? ReadLine()
    {
        if (_disposed)
            return null;

        for (int attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            try
            {
                if (!EnsureConnected())
                {
                    if (attempt < MaxRetryAttempts - 1)
                    {
                        Thread.Sleep(RetryDelayMs);
                        continue;
                    }
                    return null;
                }

                lock (_lockObject)
                {
                    if (_port?.IsOpen == true)
                    {
                        return _port.ReadLine();
                    }
                }
            }
            catch (TimeoutException)
            {
                // Read timeout is expected, return null
                return null;
            }
            catch (InvalidOperationException)
            {
                // Port was closed, try to reconnect
                if (attempt < MaxRetryAttempts - 1)
                {
                    Thread.Sleep(RetryDelayMs);
                    continue;
                }
            }
            catch (Exception)
            {
                // Other errors, try to reconnect
                if (attempt < MaxRetryAttempts - 1)
                {
                    Thread.Sleep(RetryDelayMs);
                    try
                    {
                        Connect();
                    }
                    catch
                    {
                        // Ignore reconnection errors, will retry
                    }
                    continue;
                }
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lockObject)
        {
            _disposed = true;
            try
            {
                if (_port?.IsOpen == true)
                {
                    _port.Close();
                }
            }
            catch
            {
                // Ignore errors during disposal
            }
            finally
            {
                _port?.Dispose();
                _port = null;
            }
        }
    }
}
