using System.Security.Cryptography;
using Gyropad.Core.Protocol;

namespace Gyropad.Core.Tests;

public class ProtocolTests
{
    private static readonly byte[] Token = System.Text.Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");

    private static void PutU16(byte[] b, int at, ushort v)
    {
        b[at] = (byte)v;
        b[at + 1] = (byte)(v >> 8);
    }

    private static void PutI16(byte[] b, int at, short v) => PutU16(b, at, unchecked((ushort)v));

    private static void PutU32(byte[] b, int at, uint v)
    {
        b[at] = (byte)v;
        b[at + 1] = (byte)(v >> 8);
        b[at + 2] = (byte)(v >> 16);
        b[at + 3] = (byte)(v >> 24);
    }

    private static void Seal(byte[] packet, int tagOffset)
        => PutU32(packet, tagOffset, Wire.Tag(Token, packet, tagOffset));

    [Fact]
    public void TagIsFirstFourBytesOfSha256OverTokenAndPayload()
    {
        byte[] payload = [1, 2, 3, 4, 5];
        byte[] combined = [.. Token, .. payload];
        byte[] hash = SHA256.HashData(combined);
        uint expected = (uint)(hash[0] | (hash[1] << 8) | (hash[2] << 16) | (hash[3] << 24));

        Assert.Equal(expected, Wire.Tag(Token, payload, payload.Length));
    }

    [Fact]
    public void WheelV2DecodesAllFields()
    {
        var packet = new byte[Wire.PktLen];
        packet[0] = Wire.MagicIn;
        packet[1] = Wire.Version;
        PutU16(packet, 2, 513);
        PutI16(packet, 4, -12345);
        PutU16(packet, 6, 1000);
        PutU16(packet, 8, 2000);
        PutU16(packet, 10, 3000);
        PutU16(packet, 12, 4000);
        PutU16(packet, 14, 0b1010);
        PutU32(packet, 16, 987654);
        Seal(packet, 20);

        Assert.True(InputPacketParser.TryParse(packet, out InputPacket parsed));
        Assert.Equal(InputKind.WheelV2, parsed.Kind);
        Assert.Equal(513, parsed.Sequence);
        Assert.Equal(-12345, parsed.SteerRaw);
        Assert.Equal(1000, parsed.ThrRaw);
        Assert.Equal(2000, parsed.BrkRaw);
        Assert.Equal(3000, parsed.CluRaw);
        Assert.Equal(4000, parsed.HbRaw);
        Assert.Equal(0b1010, parsed.Buttons);
        Assert.Equal(987654u, parsed.EchoMs);
        Assert.Equal(20, parsed.TagOffset);
        Assert.True(Wire.VerifyTag(Token, packet, parsed.TagOffset));
    }

    [Fact]
    public void WheelV3CarriesLookAxes()
    {
        var packet = new byte[Wire.PktLenLook];
        packet[0] = Wire.MagicIn;
        packet[1] = Wire.VersionLook;
        PutI16(packet, 20, 16384);
        PutI16(packet, 22, -16384);
        Seal(packet, 24);

        Assert.True(InputPacketParser.TryParse(packet, out InputPacket parsed));
        Assert.Equal(InputKind.WheelV3, parsed.Kind);
        Assert.Equal(16384, parsed.LookXRaw);
        Assert.Equal(-16384, parsed.LookYRaw);
    }

    [Fact]
    public void PadV3DecodesAxesButtonsAndHat()
    {
        var packet = new byte[Wire.PktPadLen];
        packet[0] = Wire.MagicPad;
        packet[1] = Wire.VersionPad;
        PutU16(packet, 2, 42);
        PutI16(packet, 4, 100);
        PutI16(packet, 6, -100);
        PutI16(packet, 8, 200);
        PutI16(packet, 10, -200);
        PutU16(packet, 12, 300);
        PutU16(packet, 14, 400);
        PutU16(packet, 16, 0b1111);
        packet[18] = 3;
        PutU32(packet, 20, 1234);
        Seal(packet, 24);

        Assert.True(InputPacketParser.TryParse(packet, out InputPacket parsed));
        Assert.Equal(InputKind.PadV3, parsed.Kind);
        Assert.Equal(42, parsed.Sequence);
        Assert.Equal(100, parsed.LxRaw);
        Assert.Equal(-200, parsed.RyRaw);
        Assert.Equal(0b1111, parsed.Buttons);
        Assert.Equal(3, parsed.Hat);
        Assert.Equal(1234u, parsed.EchoMs);
    }

    [Fact]
    public void SettingsAreScaledAndClampedByTheSender()
    {
        var packet = new byte[Wire.PktSettingsLen];
        packet[0] = Wire.MagicSettings;
        packet[1] = Wire.VersionSettings;
        PutU16(packet, 2, 750);   // 0.75
        PutU16(packet, 4, 60);    // 6.0 deg
        PutU16(packet, 6, 350);   // 35.0 deg
        PutU16(packet, 8, 540);   // lock to lock ms
        PutU16(packet, 10, 562);  // 0.562 aspect
        PutU16(packet, 12, 180);  // lock degrees
        PutU16(packet, 14, 20);   // 0.02 deadzone
        Seal(packet, 16);

        Assert.True(InputPacketParser.TryParse(packet, out InputPacket parsed));
        Assert.Equal(InputKind.Settings, parsed.Kind);
        Assert.NotNull(parsed.Settings);
        Assert.Equal(0.75f, parsed.Settings!.AssistStrength, 4);
        Assert.Equal(6.0f, parsed.Settings.AssistDeadbandDeg, 4);
        Assert.Equal(35.0f, parsed.Settings.AssistFullAtDeg, 4);
        Assert.Equal(540, parsed.Settings.LockToLockMs);
        Assert.Equal(180, parsed.Settings.LockDegrees);
        Assert.Equal(0.02f, parsed.Settings.SteerDeadzone, 4);
    }

    [Fact]
    public void HoldPacketCarriesProgress()
    {
        var packet = new byte[Wire.PktHoldLen];
        packet[0] = Wire.MagicHold;
        packet[1] = Wire.VersionHold;
        packet[2] = Wire.HoldSetup;
        packet[3] = 128;
        Seal(packet, 8);

        Assert.True(InputPacketParser.TryParse(packet, out InputPacket parsed));
        Assert.Equal(InputKind.Hold, parsed.Kind);
        Assert.Equal(Wire.HoldSetup, parsed.Hold!.What);
        Assert.Equal(128 / 255f, parsed.Hold.Progress, 4);
    }

    [Fact]
    public void FaceV4DecodesMappingsAndOffsets()
    {
        var packet = new byte[Wire.PktFaceLen];
        packet[0] = Wire.MagicFace;
        packet[1] = Wire.VersionFace;
        packet[2] = Wire.ModeWheel;
        // pad permutation 2,0,3,1 -> 0b01_11_00_10
        packet[3] = 0b01_11_00_10;
        // sys permutation 1,0,2 -> 0b10_00_01
        packet[4] = 0b10_00_01;
        packet[5] = (byte)(Wire.FaceTopSwapped | Wire.FaceClusterSwapped);
        packet[6] = Wire.FaceWheelHeld;
        packet[7] = 100; // 0.5
        packet[8] = 50;  // 1.0
        packet[9] = 75;  // 1.5
        packet[10] = 100;
        packet[11] = 50;
        PutU16(packet, 12, 900);
        packet[14] = 4; // 0.2 deg
        packet[15] = (byte)(Wire.FaceClusterLinked | Wire.FaceStickLinked);
        PutI16(packet, 16, 5000);
        for (int i = 1; i < 11; i++)
            PutI16(packet, 16 + i * 2, (short)(i * 1000));
        Seal(packet, 40);

        Assert.True(InputPacketParser.TryParse(packet, out InputPacket parsed));
        Assert.Equal(InputKind.FaceV4, parsed.Kind);
        FacePacket face = parsed.Face!;
        Assert.Equal(Wire.ModeWheel, face.Mode);
        Assert.Equal([2, 0, 3, 1], face.Pad);
        Assert.Equal([1, 0, 2], face.Sys);
        Assert.True(face.TopSwapped);
        Assert.True(face.ClusterSwapped);
        Assert.True(face.WheelHeld);
        Assert.Equal(0.5f, face.CurveK, 4);
        Assert.Equal(1.5f, face.Gamma[1], 4);
        Assert.Equal(900, face.DegPerUnit);
        Assert.Equal(0.2f, face.BacklashDeg, 4);
        Assert.True(face.ClusterLinked);
        Assert.True(face.StickLinked);
        Assert.Equal(0.5f, face.Offsets![0], 4);
        Assert.Equal(1.0f, face.Offsets[10], 4);
    }

    [Fact]
    public void TamperedPacketFailsTagVerification()
    {
        var packet = new byte[Wire.PktLen];
        packet[0] = Wire.MagicIn;
        packet[1] = Wire.Version;
        PutU16(packet, 2, 1);
        Seal(packet, 20);
        Assert.True(Wire.VerifyTag(Token, packet, 20));

        packet[4] ^= 0xFF;
        Assert.False(Wire.VerifyTag(Token, packet, 20));
    }

    [Fact]
    public void UnknownMagicIsRejected()
    {
        var packet = new byte[32];
        packet[0] = 0x99;
        Assert.False(InputPacketParser.TryParse(packet, out _));
    }
}
