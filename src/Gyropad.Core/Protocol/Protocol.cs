using System.Security.Cryptography;

namespace Gyropad.Core.Protocol;

/// <summary>
/// Wire constants for the Gyropad 2.x phone protocol ("PW2").
/// Values mirror the original Windows build; see docs/Wire.md.
/// </summary>
public static class Wire
{
    public const int PortInput = 5005;
    public const int PortDiscovery = 5006;
    public const int PortPair = 5007;
    public const int PortPairPhone = 7507;
    public const int PortOutGauge = 30000;
    public const int PortOutSim = 30001;

    public const byte MagicIn = 80;
    public const byte MagicTelem = 84;
    public const byte Version = 2;
    public const int PktLen = 24;
    public const int PktLenLook = 28;
    public const byte VersionLook = 3;
    public const byte VersionTelem = 5;
    public const int TelemLen = 16;
    public const int TelemLenV3 = 24;
    public const int TelemLenV4 = 28;

    public const int TfOutSim = 1;
    public const int TfXbox = 2;
    public const int TfCamStick = 4;

    public const int PedThrottle = 0;
    public const int PedBrake = 1;
    public const int PedClutch = 2;
    public const int PedHandbrake = 3;

    public const int LblDefault = 0;
    public const int LblDrift = 1;
    public const int LblNitro = 2;
    public const int LblBoost = 3;
    public const int LblBrake = 4;
    public const int LblL1 = 5;
    public const int LblR1 = 6;
    public const int LblL2 = 7;
    public const int LblR2 = 8;
    public const int LblFire = 9;
    public const int LblRoll = 10;
    public const int LblJump = 11;

    public const byte MagicPad = 71;
    public const byte VersionPad = 3;
    public const int PktPadLen = 28;

    public const byte MagicFace = 70;
    public const byte VersionFace = 4;
    public const int PktFaceLen = 44;
    public const float FaceOffScale = 10000f;
    public const int FaceClusterLinked = 1;
    public const int FaceStickLinked = 2;
    public const byte VersionFaceV3 = 3;
    public const int PktFaceLenV3 = 20;
    public const byte VersionFaceV2 = 2;
    public const int PktFaceLenV2 = 16;
    public const byte VersionFaceV1 = 1;
    public const int PktFaceLenV1 = 12;
    public const float FaceGammaScale = 50f;
    public const int FaceTopSwapped = 1;
    public const int FaceClusterSwapped = 2;
    public const int FaceWheelHeld = 1;

    public const int ModeAccel = 0;
    public const int ModeGyro = 1;
    public const int ModeWheel = 3;

    public const byte MagicHold = 85;
    public const byte VersionHold = 1;
    public const int PktHoldLen = 12;
    public const int HoldNone = 0;
    public const int HoldMode = 1;
    public const int HoldSetup = 2;
    public const int HoldArrange = 3;

    public const byte MagicSettings = 83;
    public const byte VersionSettings = 4;
    public const int PktSettingsLen = 20;

    public const int BUp = 0;
    public const int BDown = 1;
    public const int BIgnition = 2;
    public const int BLights = 3;
    public const int BHorn = 4;
    public const int BCamera = 5;
    public const int BLook = 6;
    public const int BMap = 7;
    public const int BDpadUp = 8;
    public const int BDpadLeft = 9;
    public const int BDpadDown = 10;
    public const int BDpadRight = 11;
    public const int BFaceTri = 12;
    public const int BFaceSq = 13;
    public const int BFaceX = 14;
    public const int BFaceCircle = 15;

    public const int PadCross = 1;
    public const int PadCircle = 2;
    public const int PadSquare = 4;
    public const int PadTriangle = 5;
    public const int PadL1 = 7;
    public const int PadR1 = 8;
    public const int PadCreate = 11;
    public const int PadOptions = 12;
    public const int PadHome = 13;
    public const int PadL3 = 14;
    public const int PadR3 = 15;

    public const int HapNone = 0;
    public const int HapGear = 1;
    public const int HapTraction = 2;
    public const int HapRedline = 3;
    public const int HapAbs = 4;
    public const int HapTest = 5;

    public static int PedAbsent(int pedal) => 1 << pedal;

    public static int PedDigital(int pedal) => 1 << (pedal + 4);

    public static int PedalLabels(int clutch, int handbrake)
        => (clutch & 0xF) | ((handbrake & 0xF) << 4);

    public static int PedalLabels2(int throttle, int brake)
        => (throttle & 0xF) | ((brake & 0xF) << 4);

    /// <summary>
    /// First four bytes of SHA-256(token || packet[0..len]) as a little-endian
    /// uint32. The token is the ASCII form of the 32-hex-character pairing token.
    /// </summary>
    public static uint Tag(byte[] token, ReadOnlySpan<byte> packet, int len)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (len < 0 || len > packet.Length)
            throw new ArgumentOutOfRangeException(nameof(len));

        var buffer = new byte[token.Length + len];
        Buffer.BlockCopy(token, 0, buffer, 0, token.Length);
        packet[..len].CopyTo(buffer.AsSpan(token.Length));
        var hash = SHA256.HashData(buffer);
        return (uint)(hash[0] | (hash[1] << 8) | (hash[2] << 16) | (hash[3] << 24));
    }

    public static bool VerifyTag(byte[] token, ReadOnlySpan<byte> packet, int len)
        => ReadUInt32(packet, len) == Tag(token, packet, len);

    public static ushort ReadUInt16(ReadOnlySpan<byte> b, int at)
        => (ushort)(b[at] | (b[at + 1] << 8));

    public static short ReadInt16(ReadOnlySpan<byte> b, int at)
        => (short)(b[at] | (b[at + 1] << 8));

    public static uint ReadUInt32(ReadOnlySpan<byte> b, int at)
        => (uint)(b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24));
}
