using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Gyropad.Core.Telemetry;

/// <summary>
/// Live for Speed OutGauge listener (UDP 30000): gear, RPM, speed and the
/// shift/TC/ABS light flags used by the phone's telemetry display.
/// </summary>
public sealed class OutGauge
{
    public volatile bool Live;
    public volatile int Gear = 255;
    public volatile int Rpm;
    public volatile int SpeedKmhX10;
    public volatile uint ShowLights;
    public volatile int LastAt;
    public volatile string Car = "";

    public int Port = 30000;

    private Thread? _thread;
    private volatile bool _run;
    private UdpClient? _udp;

    public bool Redline => (ShowLights & 1) != 0;
    public bool Traction => (ShowLights & 0x10) != 0;
    public bool Abs => (ShowLights & 0x400) != 0;

    public void Start()
    {
        if (_run)
            return;
        _run = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "gyropad-outgauge" };
        _thread.Start();
    }

    public void Stop()
    {
        _run = false;
        try
        {
            _udp?.Close();
        }
        catch
        {
            // shutting down
        }
    }

    private void Loop()
    {
        try
        {
            _udp = new UdpClient(Port);
        }
        catch
        {
            return;
        }

        try
        {
            var remote = new IPEndPoint(IPAddress.Any, Port);
            while (_run)
            {
                byte[] data;
                try
                {
                    data = _udp.Receive(ref remote);
                }
                catch
                {
                    if (!_run)
                        break;
                    continue;
                }

                if (data.Length < 92)
                    continue;
                try
                {
                    int i;
                    for (i = 0; i < 4 && data[4 + i] != 0; i++)
                    {
                    }

                    Car = Encoding.ASCII.GetString(data, 4, i);
                    Gear = data[10];
                    float speed = BitConverter.ToSingle(data, 12);
                    float rpm = BitConverter.ToSingle(data, 16);
                    ShowLights = BitConverter.ToUInt32(data, 44);
                    Rpm = (int)rpm;
                    SpeedKmhX10 = (int)(speed * 36f);
                    LastAt = Environment.TickCount;
                    Live = true;
                }
                catch
                {
                    // malformed packet
                }
            }
        }
        finally
        {
            Live = false;
            try
            {
                _udp?.Close();
            }
            catch
            {
                // shutting down
            }
        }
    }

    public void Expire()
    {
        if (Live && Environment.TickCount - LastAt > 2000)
        {
            Live = false;
            Gear = 255;
        }
    }
}
