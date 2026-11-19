using System.Text;

namespace Gyropad.Core.Linux;

/// <summary>
/// Constants and pure mapping helpers for Linux <c>uinput</c>. Kept separate
/// from the syscall layer so the ABI can be unit-tested on any OS.
/// </summary>
/// <remarks>
/// Layouts and ioctl numbers follow <c>linux/uinput.h</c> and
/// <c>linux/input-event-codes.h</c> for 64-bit little-endian kernels
/// (x86_64 and arm64 both use the generic <c>_IOC</c> encoding).
/// </remarks>
internal static class Uinput
{
    public const string DevicePath = "/dev/uinput";

    /// <summary><c>struct input_event</c>: two 64-bit time fields, type, code, value.</summary>
    public const int EventSize = 24;

    /// <summary><c>struct input_absinfo</c>: value, min, max, fuzz, flat, resolution.</summary>
    public const int AbsInfoSize = 24;

    /// <summary><c>struct uinput_setup</c>: input_id (8) + name[80] + effects max (4).</summary>
    public const int SetupSize = 92;

    /// <summary><c>struct uinput_abs_setup</c>: code (2) + pad (2) + absinfo (24).</summary>
    public const int AbsSetupSize = 28;

    /// <summary>Where the kernel expects <c>name[0]</c> inside <c>struct uinput_setup</c>.</summary>
    public const int SetupNameOffset = 8;

    /// <summary>Where the kernel expects <c>ff_effects_max</c> inside <c>struct uinput_setup</c>.</summary>
    public const int SetupEffectsOffset = 88;

    // Event types.
    public const ushort EvSyn = 0x00;
    public const ushort EvKey = 0x01;
    public const ushort EvAbs = 0x03;
    public const ushort SynReport = 0x00;

    // Absolute axes.
    public const ushort AbsX = 0x00;
    public const ushort AbsY = 0x01;
    public const ushort AbsZ = 0x02;
    public const ushort AbsRx = 0x03;
    public const ushort AbsRy = 0x04;
    public const ushort AbsRz = 0x05;
    public const ushort AbsHat0X = 0x10;
    public const ushort AbsHat0Y = 0x11;

    // Gamepad keys, in the order the kernel's xpad driver reports them. That
    // order is what SDL's built-in Xbox mapping assumes.
    public const ushort BtnSouth = 0x130;  // A
    public const ushort BtnEast = 0x131;   // B
    public const ushort BtnNorth = 0x133;  // X
    public const ushort BtnWest = 0x134;   // Y
    public const ushort BtnTL = 0x136;
    public const ushort BtnTR = 0x137;
    public const ushort BtnSelect = 0x13a;
    public const ushort BtnStart = 0x13b;
    public const ushort BtnMode = 0x13c;
    public const ushort BtnThumbL = 0x13d;
    public const ushort BtnThumbR = 0x13e;

    /// <summary>
    /// Sixteen wheel buttons exposed as a generic joystick, mirroring vJoy's
    /// button 1..16. Codes ascend so evdev keeps that order.
    /// </summary>
    public static readonly ushort[] WheelButtons =
    [
        0x120, // BTN_TRIGGER
        0x121, // BTN_THUMB
        0x122, // BTN_THUMB2
        0x123, // BTN_TOP
        0x124, // BTN_TOP2
        0x125, // BTN_PINKIE
        0x126, // BTN_BASE
        0x127, // BTN_BASE2
        0x128, // BTN_BASE3
        0x129, // BTN_BASE4
        0x12a, // BTN_BASE5
        0x12b, // BTN_BASE6
        0x2c0, // BTN_TRIGGER_HAPPY1
        0x2c1,
        0x2c2,
        0x2c3
    ];

    /// <summary>Xbox 360 button bit to evdev key, matching ViGEmBridge's mask.</summary>
    public static readonly (int Bit, ushort Code)[] PadButtons =
    [
        (0x1000, BtnSouth),
        (0x2000, BtnEast),
        (0x4000, BtnNorth),
        (0x8000, BtnWest),
        (0x0100, BtnTL),
        (0x0200, BtnTR),
        (0x0020, BtnSelect),
        (0x0010, BtnStart),
        (0x0400, BtnMode),
        (0x0040, BtnThumbL),
        (0x0080, BtnThumbR)
    ];

    private const uint IocWrite = 1;
    private const int UinputIoctlBase = 'U';

    /// <summary><c>_IOC(dir, type, nr, size)</c> for the generic encoding.</summary>
    public static uint Ioctl(uint direction, int type, int number, int size)
        => (direction << 30)
           | (((uint)size & 0x3FFF) << 16)
           | (((uint)type & 0xFF) << 8)
           | ((uint)number & 0xFF);

    public static uint IoW(int type, int number, int size) => Ioctl(IocWrite, type, number, size);

    public static uint Io(int type, int number) => Ioctl(0, type, number, 0);

    public static readonly uint DevCreate = Io(UinputIoctlBase, 1);
    public static readonly uint DevDestroy = Io(UinputIoctlBase, 2);
    public static readonly uint DevSetup = IoW(UinputIoctlBase, 3, SetupSize);
    public static readonly uint AbsSetup = IoW(UinputIoctlBase, 4, AbsSetupSize);
    public static readonly uint SetEvBit = IoW(UinputIoctlBase, 100, 4);
    public static readonly uint SetKeyBit = IoW(UinputIoctlBase, 101, 4);
    public static readonly uint SetRelBit = IoW(UinputIoctlBase, 102, 4);
    public static readonly uint SetAbsBit = IoW(UinputIoctlBase, 103, 4);

    /// <summary>One axis plus the range clients will see.</summary>
    public readonly record struct Axis(ushort Code, int Min, int Max);

    /// <summary>
    /// Encodes <c>struct uinput_setup</c> exactly as the kernel reads it.
    /// </summary>
    /// <remarks>
    /// Field order is part of the ABI: <c>input_id</c>, then <c>name[80]</c>,
    /// then <c>ff_effects_max</c>. Encoding it as a marshalled struct with the
    /// fields in a different order still yields the same 92-byte size, so the
    /// ioctl number looks correct while the kernel reads a zero at
    /// <c>name[0]</c> and rejects the description with EINVAL.
    /// </remarks>
    public static byte[] BuildSetup(string name, ushort vendor, ushort product, ushort version)
    {
        var buffer = new byte[SetupSize];

        PutUInt16(buffer, 0, 0x03); // BUS_USB
        PutUInt16(buffer, 2, vendor);
        PutUInt16(buffer, 4, product);
        PutUInt16(buffer, 6, version);

        // The name must be terminated inside its 80 bytes; byte 79 stays zero.
        byte[] text = Encoding.UTF8.GetBytes(name);
        int length = Math.Min(text.Length, 79);
        Array.Copy(text, 0, buffer, SetupNameOffset, length);

        PutUInt32(buffer, SetupEffectsOffset, 0);
        return buffer;
    }

    /// <summary>Encodes <c>struct uinput_abs_setup</c> for one axis.</summary>
    public static byte[] BuildAbsSetup(Axis axis)
    {
        var buffer = new byte[AbsSetupSize];

        PutUInt16(buffer, 0, axis.Code);
        // Bytes 2-3 are the kernel's alignment padding and stay zero.
        PutInt32(buffer, 4, 0);         // value
        PutInt32(buffer, 8, axis.Min);  // minimum
        PutInt32(buffer, 12, axis.Max); // maximum
        PutInt32(buffer, 16, 0);        // fuzz
        PutInt32(buffer, 20, 0);        // flat
        PutInt32(buffer, 24, 0);        // resolution
        return buffer;
    }

    private static void PutUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }

    private static void PutUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static void PutInt32(byte[] buffer, int offset, int value)
        => PutUInt32(buffer, offset, (uint)value);


    /// <summary>Signed stick axis: -1..1 to -32767..32767, centred on zero.</summary>
    public static int Stick(double value)
    {
        double scaled = Clamp(value, -1.0, 1.0) * 32767.0;
        return (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Vertical stick axis. The network layer keeps the Windows convention
    /// (positive is up, as XInput and vJoy report it), while Linux evdev ABS_Y
    /// is positive down. Every vertical stick value crosses this boundary.
    /// </summary>
    public static int StickY(double value) => Stick(0.0 - value);

    /// <summary>Unipolar pedal axis: 0..1 to 0..32767.</summary>
    public static int Pedal(double value)
        => (int)Math.Round(Clamp(value, 0.0, 1.0) * 32767.0, MidpointRounding.AwayFromZero);

    /// <summary>Trigger as the Xbox driver reports it: 0..255.</summary>
    public static int Trigger(double value)
        => (int)Math.Round(Clamp(value, 0.0, 1.0) * 255.0, MidpointRounding.AwayFromZero);

    /// <summary>D-pad bit mask (up 1, down 2, left 4, right 8) to hat axes.</summary>
    public static (int X, int Y) Hat(int buttons)
    {
        int x = ((buttons & 0x8) != 0 ? 1 : 0) - ((buttons & 0x4) != 0 ? 1 : 0);
        int y = ((buttons & 0x2) != 0 ? 1 : 0) - ((buttons & 0x1) != 0 ? 1 : 0);
        return (x, y);
    }

    private static double Clamp(double value, double low, double high)
    {
        if (double.IsNaN(value))
            return 0.0;
        if (value < low)
            return low;
        if (value > high)
            return high;
        return value;
    }
}
