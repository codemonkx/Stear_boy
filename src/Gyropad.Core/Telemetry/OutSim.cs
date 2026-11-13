using System.Net;
using System.Net.Sockets;

namespace Gyropad.Core.Telemetry;

/// <summary>
/// Live for Speed OutSim listener: derives slip angle and yaw rate, which drive
/// the drift assist and the phone's traction display.
/// </summary>
public sealed class OutSim
{
    public volatile bool Live;
    public volatile int LastAt;
    public volatile float SlipDeg;
    public volatile float YawRateDeg;
    public volatile float SpeedMs;

    public volatile bool Invert;
    public volatile int Port = 30001;

    private Thread? _thread;
    private volatile bool _run;
    private UdpClient? _udp;

    public void Start()
    {
        if (_run)
            return;
        _run = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "gyropad-outsim" };
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

    public void Rebind(int port)
    {
        Port = port;
        Stop();
        Thread.Sleep(60);
        _run = false;
        Start();
    }

    private void Loop()
    {
        int port = Port;
        try
        {
            _udp = new UdpClient(port);
        }
        catch
        {
            return;
        }

        try
        {
            var remote = new IPEndPoint(IPAddress.Any, port);
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

                if (data.Length >= 64)
                {
                    try
                    {
                        Decode(data);
                    }
                    catch
                    {
                        // malformed packet
                    }
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

    private void Decode(byte[] d)
    {
        float yawRate = BitConverter.ToSingle(d, 12);
        float heading = BitConverter.ToSingle(d, 16);
        float velX = BitConverter.ToSingle(d, 40);
        float velY = BitConverter.ToSingle(d, 44);

        float speed = MathF.Sqrt(velX * velX + velY * velY);
        SpeedMs = speed;

        float slip = 0f;
        if (speed > 3f)
        {
            double headingToTravel = Math.Atan2(0f - velX, velY);
            double delta;
            for (delta = headingToTravel - heading; delta > Math.PI; delta -= Math.PI * 2.0)
            {
            }

            for (; delta < -Math.PI; delta += Math.PI * 2.0)
            {
            }

            slip = (float)((0.0 - delta) * 180.0 / Math.PI);
        }

        float sign = Invert ? -1f : 1f;
        SlipDeg = slip * sign;
        YawRateDeg = (float)(yawRate * 180.0 / Math.PI) * sign;
        LastAt = Environment.TickCount;
        Live = true;
    }

    public void Expire()
    {
        if (Live && Environment.TickCount - LastAt > 2000)
        {
            Live = false;
            SlipDeg = 0f;
            YawRateDeg = 0f;
            SpeedMs = 0f;
        }
    }
}
