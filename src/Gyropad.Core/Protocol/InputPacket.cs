namespace Gyropad.Core.Protocol;

public enum InputKind
{
    WheelV2,
    WheelV3,
    PadV3,
    Settings,
    Hold,
    FaceV1,
    FaceV2,
    FaceV3,
    FaceV4
}

public sealed class FacePacket
{
    public int Mode;
    public int[] Pad = [0, 1, 2, 3];
    public int[] Sys = [0, 1, 2];
    public bool TopSwapped;
    public bool ClusterSwapped;
    public bool WheelHeld;
    public float[] Gamma = [1f, 1f, 1f, 1f];
    public float CurveK;
    public ushort DegPerUnit;
    public float BacklashDeg;
    public bool ClusterLinked;
    public bool StickLinked;
    public float[]? Offsets;
    public int Version;
}

public sealed class SettingsPacket
{
    public float AssistStrength;
    public float AssistDeadbandDeg;
    public float AssistFullAtDeg = 35f;
    public int LockToLockMs;
    public float Aspect = 1f;
    public int LockDegrees;
    public float SteerDeadzone;
}

public sealed class HoldPacket
{
    public int What;
    public float Progress;
}

/// <summary>
/// One decoded phone datagram. Raw values are preserved exactly as sent so the
/// input model and the game profiles can be ported without retuning.
/// </summary>
public sealed class InputPacket
{
    public InputKind Kind;

    public int Sequence;
    public int Buttons;
    public uint EchoMs;

    // Wheel
    public short SteerRaw;
    public ushort ThrRaw;
    public ushort BrkRaw;
    public ushort CluRaw;
    public ushort HbRaw;
    public short LookXRaw;
    public short LookYRaw;

    // Pad
    public short LxRaw;
    public short LyRaw;
    public short RxRaw;
    public short RyRaw;
    public ushort LtRaw;
    public ushort RtRaw;
    public int Hat = 8;

    public SettingsPacket? Settings;
    public HoldPacket? Hold;
    public FacePacket? Face;

    public int TagOffset;
}

public static class InputPacketParser
{
    /// <summary>
    /// Decodes a datagram without touching state. Returns false for an unknown
    /// magic/version/length combination (the caller should then ignore it).
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> p, out InputPacket packet)
    {
        packet = new InputPacket();

        if (p.Length == Wire.PktLen && p[0] == Wire.MagicIn && p[1] == Wire.Version)
        {
            packet.Kind = InputKind.WheelV2;
            packet.TagOffset = 20;
            packet.Sequence = Wire.ReadUInt16(p, 2);
            packet.SteerRaw = Wire.ReadInt16(p, 4);
            packet.ThrRaw = Wire.ReadUInt16(p, 6);
            packet.BrkRaw = Wire.ReadUInt16(p, 8);
            packet.CluRaw = Wire.ReadUInt16(p, 10);
            packet.HbRaw = Wire.ReadUInt16(p, 12);
            packet.Buttons = Wire.ReadUInt16(p, 14);
            packet.EchoMs = Wire.ReadUInt32(p, 16);
            return true;
        }

        if (p.Length == Wire.PktLenLook && p[0] == Wire.MagicIn && p[1] == Wire.VersionLook)
        {
            packet.Kind = InputKind.WheelV3;
            packet.TagOffset = 24;
            packet.Sequence = Wire.ReadUInt16(p, 2);
            packet.SteerRaw = Wire.ReadInt16(p, 4);
            packet.ThrRaw = Wire.ReadUInt16(p, 6);
            packet.BrkRaw = Wire.ReadUInt16(p, 8);
            packet.CluRaw = Wire.ReadUInt16(p, 10);
            packet.HbRaw = Wire.ReadUInt16(p, 12);
            packet.Buttons = Wire.ReadUInt16(p, 14);
            packet.EchoMs = Wire.ReadUInt32(p, 16);
            packet.LookXRaw = Wire.ReadInt16(p, 20);
            packet.LookYRaw = Wire.ReadInt16(p, 22);
            return true;
        }

        if (p.Length == Wire.PktPadLen && p[0] == Wire.MagicPad && p[1] == Wire.VersionPad)
        {
            packet.Kind = InputKind.PadV3;
            packet.TagOffset = 24;
            packet.Sequence = Wire.ReadUInt16(p, 2);
            packet.LxRaw = Wire.ReadInt16(p, 4);
            packet.LyRaw = Wire.ReadInt16(p, 6);
            packet.RxRaw = Wire.ReadInt16(p, 8);
            packet.RyRaw = Wire.ReadInt16(p, 10);
            packet.LtRaw = Wire.ReadUInt16(p, 12);
            packet.RtRaw = Wire.ReadUInt16(p, 14);
            packet.Buttons = Wire.ReadUInt16(p, 16);
            packet.Hat = p[18];
            packet.EchoMs = Wire.ReadUInt32(p, 20);
            return true;
        }

        if (p.Length == Wire.PktSettingsLen && p[0] == Wire.MagicSettings && p[1] == Wire.VersionSettings)
        {
            packet.Kind = InputKind.Settings;
            packet.TagOffset = 16;
            packet.Settings = new SettingsPacket
            {
                AssistStrength = Wire.ReadUInt16(p, 2) / 1000f,
                AssistDeadbandDeg = Wire.ReadUInt16(p, 4) / 10f,
                AssistFullAtDeg = Wire.ReadUInt16(p, 6) / 10f,
                LockToLockMs = Wire.ReadUInt16(p, 8),
                Aspect = Wire.ReadUInt16(p, 10) / 1000f,
                LockDegrees = Wire.ReadUInt16(p, 12),
                SteerDeadzone = Wire.ReadUInt16(p, 14) / 1000f
            };
            return true;
        }

        if (p.Length == Wire.PktHoldLen && p[0] == Wire.MagicHold && p[1] == Wire.VersionHold)
        {
            packet.Kind = InputKind.Hold;
            packet.TagOffset = 8;
            packet.Hold = new HoldPacket
            {
                What = p[2],
                Progress = p[3] / 255f
            };
            return true;
        }

        if (p[0] == Wire.MagicFace)
        {
            FacePacket? face = p.Length switch
            {
                Wire.PktFaceLen when p[1] == Wire.VersionFace => ParseFace(p, 4, 40),
                Wire.PktFaceLenV3 when p[1] == Wire.VersionFaceV3 => ParseFace(p, 3, 16),
                Wire.PktFaceLenV2 when p[1] == Wire.VersionFaceV2 => ParseFace(p, 2, 12),
                Wire.PktFaceLenV1 when p[1] == Wire.VersionFaceV1 => ParseFace(p, 1, 8),
                _ => null
            };
            if (face == null)
                return false;

            packet.Kind = face.Version switch
            {
                4 => InputKind.FaceV4,
                3 => InputKind.FaceV3,
                2 => InputKind.FaceV2,
                _ => InputKind.FaceV1
            };
            packet.TagOffset = p.Length switch
            {
                Wire.PktFaceLen => 40,
                Wire.PktFaceLenV3 => 16,
                Wire.PktFaceLenV2 => 12,
                _ => 8
            };
            packet.Face = face;
            return true;
        }

        return false;
    }

    private static FacePacket ParseFace(ReadOnlySpan<byte> p, int version, int tagOffset)
    {
        var face = new FacePacket { Version = version };

        byte padBits = p[3];
        byte sysBits = p[4];
        byte flags = p[5];
        face.Mode = p[2];
        for (int k = 0; k < 4; k++)
            face.Pad[k] = (padBits >> (k * 2)) & 3;
        for (int k = 0; k < 3; k++)
            face.Sys[k] = (sysBits >> (k * 2)) & 3;
        face.TopSwapped = (flags & 1) != 0;
        face.ClusterSwapped = (flags & 2) != 0;
        face.WheelHeld = (p[6] & 1) != 0;

        if (p.Length >= 16)
        {
            for (int i = 0; i < 4; i++)
            {
                float g = p[8 + i] / Wire.FaceGammaScale;
                face.Gamma[i] = g > 0.05f && g < 6f ? g : 1f;
            }
        }

        if (p.Length >= 20)
        {
            float k = p[7] / 200f;
            face.CurveK = k > 0.05f && k <= 1.2f ? k : 0f;
            face.DegPerUnit = Wire.ReadUInt16(p, 12);
            face.BacklashDeg = p[14] / 20f;
        }

        if (!IsPermutation(face.Pad, 4))
            face.Pad = [0, 1, 2, 3];
        if (!IsPermutation(face.Sys, 3))
            face.Sys = [0, 1, 2];

        if (version >= 4)
        {
            var offsets = new float[11];
            for (int i = 0; i < offsets.Length; i++)
                offsets[i] = Wire.ReadInt16(p, 16 + i * 2) / Wire.FaceOffScale;
            face.Offsets = offsets;
            face.ClusterLinked = (p[15] & Wire.FaceClusterLinked) != 0;
            face.StickLinked = (p[15] & Wire.FaceStickLinked) != 0;
        }

        _ = tagOffset;
        return face;
    }

    private static bool IsPermutation(int[] a, int n)
    {
        var seen = new bool[n];
        foreach (int v in a)
        {
            if (v < 0 || v >= n || seen[v])
                return false;
            seen[v] = true;
        }
        return true;
    }
}
