namespace Gyropad.Core.Input;

/// <summary>
/// Wheel state as reported by the phone, plus the counters the UI reads.
/// </summary>
public sealed class Wheel
{
    public volatile int SteerRaw;
    public volatile int ThrRaw;
    public volatile int BrkRaw;
    public volatile int CluRaw;
    public volatile int HbRaw;
    public volatile int Buttons;
    public volatile int Rate;
    public volatile int TickAtLastPacket;
    public volatile int Rejected;

    public double SteerOut;

    public volatile float LookX;
    public volatile float LookY;

    public double Steer => SteerRaw / 32767.0;
    public double Thr => ThrRaw / 65535.0;
    public double Brk => BrkRaw / 65535.0;
    public double Clu => CluRaw / 65535.0;
    public double Hb => HbRaw / 65535.0;

    public void Zero()
    {
        SteerRaw = 0;
        ThrRaw = 0;
        BrkRaw = 0;
        CluRaw = 0;
        HbRaw = 0;
        Buttons = 0;
    }
}
