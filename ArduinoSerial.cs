using System;
using System.IO.Ports;

namespace DaevaMini;

public class ArduinoSerial : IDisposable
{
    private readonly SerialPort _port;

    public ArduinoSerial(string portName)
    {
        _port = new SerialPort(portName, 115200)
        {
            NewLine = "\n",
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };

        _port.Open();
    }

    public void Send(string command)
    {
        _port.WriteLine(command);
    }

    public string ReadLine()
    {
        return _port.ReadLine();
    }

    public void Dispose()
    {
        if (_port.IsOpen)
            _port.Close();
    }
}
