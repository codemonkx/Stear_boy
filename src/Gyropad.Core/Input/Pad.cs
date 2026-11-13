namespace Gyropad.Core.Input;

/// <summary>
/// Gamepad state as reported by the phone: six axes, 16 buttons, one hat.
/// Raw values are kept so the port can reproduce the original scaling exactly.
/// </summary>
public sealed class Pad
{
    public volatile int LxRaw;
    public volatile int LyRaw;
    public volatile int RxRaw;
    public volatile int RyRaw;
    public volatile int LtRaw;
    public volatile int RtRaw;
    public volatile int Buttons;
    public volatile int Hat = 8;

    public double Lx => LxRaw / 32767.0;
    public double Ly => LyRaw / 32767.0;
    public double Rx => RxRaw / 32767.0;
    public double Ry => RyRaw / 32767.0;
    public double Lt => LtRaw / 65535.0;
    public double Rt => RtRaw / 65535.0;

    public void Zero()
    {
        LxRaw = 0;
        LyRaw = 0;
        RxRaw = 0;
        RyRaw = 0;
        LtRaw = 0;
        RtRaw = 0;
        Buttons = 0;
        Hat = 8;
    }
}
