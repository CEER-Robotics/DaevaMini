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
                        // Start each command from a clean slate. The firmware emits
                        // unsolicited lines (DONE when a pour finishes, OK READY when it
                        // re-enters WAIT); left in the buffer they would be read back as
                        // the answer to this command.
                        _port.DiscardInBuffer();
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

    /// <summary>
    /// Writes one command and reads its answer without letting go of the port in between.
    /// </summary>
    /// <remarks>
    /// <see cref="Send"/> clears the input buffer before every write, so a command sent from
    /// another thread - an idle tint on a page change, a tap keep-alive - landing between a
    /// write and its read would discard the answer to that write. Holding the lock across the
    /// whole exchange is what makes an acknowledgement trustworthy. Only one exchange is
    /// attempted here: repeating is the caller's decision, so the port is never held for
    /// several seconds and a keep-alive on another thread is not starved behind a retry.
    /// </remarks>
    /// <param name="isAnswer">Recognises the reply to this command. Unsolicited lines (DONE,
    /// OK READY) are stepped over, up to <paramref name="maxLines"/> of them.</param>
    /// <param name="answer">The reply line, or null when nothing answered in time.</param>
    /// <returns>False when the command could not be written at all.</returns>
    public bool Exchange(string command, int maxLines, Func<string, bool> isAnswer, out string? answer)
    {
        answer = null;

        if (string.IsNullOrEmpty(command) || _disposed)
            return false;

        if (!EnsureConnected())
            return false;

        lock (_lockObject)
        {
            if (_port?.IsOpen != true)
                return false;

            try
            {
                _port.DiscardInBuffer();
                _port.WriteLine(command);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ArduinoSerial] Exchange write failed: {ex.GetType().Name} - {ex.Message}");
                return false;
            }

            for (int i = 0; i < maxLines; i++)
            {
                string line;
                try
                {
                    line = _port.ReadLine();
                }
                catch (TimeoutException)
                {
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ArduinoSerial] Exchange read failed: {ex.GetType().Name} - {ex.Message}");
                    return true;
                }

                if (isAnswer(line))
                {
                    answer = line;
                    return true;
                }

                Console.WriteLine($"[ArduinoSerial] Ignoring unsolicited line while waiting for an ack: {line.Trim()}");
            }

            return true;
        }
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
