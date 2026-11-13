namespace Gyropad.Core.Titles;

/// <summary>
/// Per-title capabilities and button remapping. Defaults match the original
/// build's generic profile; the supported-title table lives in <c>Games</c>.
/// </summary>
public sealed class GameProfile
{
    public string Exe = "";
    public string Name = "";
    public string Prefix = "";

    public bool NeedsPad;
    public bool WheelUntested;
    public bool WheelNeedsMapping;
    public GameProfile? PadFallback;

    public ushort Handbrake = 4096;
    public ushort Clutch = 16384;
    public ushort Camera = 32768;
    public ushort GearUp = 512;
    public ushort GearDown = 256;
    public ushort Horn = 128;
    public ushort Lights = 2;
    public ushort Map = 32;
    public ushort Ignition = 64;
    public ushort Pause = 16;

    public bool HasClutch = true;
    public bool HasHandbrake = true;
    public bool HasHorn = true;
    public bool HasIgnition = true;
    public bool HasLights = true;
    public bool HasMap = true;

    public int ThrottleLabel;
    public int BrakeLabel;
    public int ClutchLabel;
    public int HandbrakeLabel;

    public bool HasGears = true;

    public double SteerAntiDeadzone;
    public double ThrottleAntiDeadzone;
    public double BrakeAntiDeadzone;

    public bool ShareFaceButtons = true;
    public bool DigitalThrottle;
    public bool DigitalBrake;
    public bool BrakeAxisFromHandbrake;
    public bool DpadDrivesStick = true;
    public bool CamIsStick;

    public ushort FaceY;
    public ushort FaceX;
    public ushort FaceA;
    public ushort FaceB;

    public bool ShiftWithStick;
    public ushort ShiftHold;

    public bool DigitalClutch = true;
    public bool DigitalHandbrake = true;
    public ushort ThrottleButton = 512;
    public ushort BrakeButton = 256;
    public double DigitalAt = 0.25;

    public GameProfile Clone() => (GameProfile)MemberwiseClone();
}
