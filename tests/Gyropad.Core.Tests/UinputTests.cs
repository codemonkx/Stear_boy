using System.Buffers.Binary;
using System.Text;
using Gyropad.Core.Linux;

namespace Gyropad.Core.Tests;

/// <summary>
/// ABI and mapping checks for the uinput layer. Device creation itself can only
/// be exercised on a Linux host; the graceful-degradation path is covered here.
/// </summary>
public class UinputTests
{
    [Fact]
    public void IoctlNumbersMatchTheKernelHeaders()
    {
        Assert.Equal(0x40045564u, Uinput.SetEvBit);      // _IOW('U', 100, int)
        Assert.Equal(0x40045565u, Uinput.SetKeyBit);     // _IOW('U', 101, int)
        Assert.Equal(0x40045567u, Uinput.SetAbsBit);     // _IOW('U', 103, int)
        Assert.Equal(0x405c5503u, Uinput.DevSetup);      // _IOW('U', 3, struct uinput_setup)
        Assert.Equal(0x401c5504u, Uinput.AbsSetup);      // _IOW('U', 4, struct uinput_abs_setup)
        Assert.Equal(0x00005501u, Uinput.DevCreate);     // _IO ('U', 1)
        Assert.Equal(0x00005502u, Uinput.DevDestroy);    // _IO ('U', 2)
    }

    [Fact]
    public void AbiSizesMatchTheKernelStructs()
    {
        Assert.Equal(24, Uinput.EventSize);    // input_event, 64-bit: 2 * long + u16 + u16 + s32
        Assert.Equal(24, Uinput.AbsInfoSize);  // 6 * s32
        Assert.Equal(92, Uinput.SetupSize);    // input_id (8) + char[80] + u32
        Assert.Equal(28, Uinput.AbsSetupSize); // u16 + pad + absinfo
    }

    [Fact]
    public void SetupBufferUsesTheKernelFieldOrder()
    {
        byte[] buffer = Uinput.BuildSetup("Gyropad Wheel", 0x1209, 0x0001, 0x0007);

        Assert.Equal(Uinput.SetupSize, buffer.Length);
        Assert.Equal(0x0003, U16(buffer, 0)); // BUS_USB
        Assert.Equal(0x1209, U16(buffer, 2)); // vendor
        Assert.Equal(0x0001, U16(buffer, 4)); // product
        Assert.Equal(0x0007, U16(buffer, 6)); // version

        // struct uinput_setup { struct input_id id; char name[80]; __u32 ff_effects_max; }
        // The kernel rejects the whole description when name[0] is zero. A struct
        // whose field order put ff_effects_max before the name still had the same
        // 92-byte size, so the ioctl number looked right but creation failed EINVAL.
        Assert.NotEqual(0, buffer[Uinput.SetupNameOffset]);
        Assert.Equal("Gyropad Wheel", Encoding.UTF8.GetString(buffer, 8, 13));
        Assert.Equal(0, buffer[8 + 13]); // NUL terminator
        Assert.Equal(0u, U32(buffer, Uinput.SetupEffectsOffset));
    }

    [Fact]
    public void LongDeviceNamesAreTruncatedInsideTheirField()
    {
        byte[] buffer = Uinput.BuildSetup(new string('x', 120), 1, 2, 3);

        Assert.Equal(Uinput.SetupSize, buffer.Length);
        Assert.Equal((byte)'x', buffer[8]);
        Assert.Equal((byte)'x', buffer[8 + 78]);
        Assert.Equal(0, buffer[8 + 79]); // still terminated inside name[80]
        Assert.Equal(0u, U32(buffer, Uinput.SetupEffectsOffset));
    }

    [Fact]
    public void AbsSetupBufferMatchesTheKernelLayout()
    {
        byte[] buffer = Uinput.BuildAbsSetup(new Uinput.Axis(Uinput.AbsZ, -32767, 32767));

        Assert.Equal(Uinput.AbsSetupSize, buffer.Length);
        Assert.Equal(Uinput.AbsZ, U16(buffer, 0));
        Assert.Equal(0, U16(buffer, 2));      // alignment padding
        Assert.Equal(0, S32(buffer, 4));      // value
        Assert.Equal(-32767, S32(buffer, 8)); // minimum
        Assert.Equal(32767, S32(buffer, 12)); // maximum
        Assert.Equal(0, S32(buffer, 16));     // fuzz
        Assert.Equal(0, S32(buffer, 20));     // flat
        Assert.Equal(0, S32(buffer, 24));     // resolution
    }

    private static ushort U16(byte[] buffer, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset));

    private static uint U32(byte[] buffer, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset));

    private static int S32(byte[] buffer, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset));

    [Theory]
    [InlineData(-32767, -1.0, -32767)]
    [InlineData(0, 0.0, 0)]
    [InlineData(16384, 0.5, 16384)]
    [InlineData(32767, 1.0, 32767)]
    public void WheelSteerMapsToASignedAxis(int rawPhoneValue, double expected, int expectedAxis)
    {
        double normalised = rawPhoneValue / 32767.0;
        Assert.Equal(expected, normalised, 3);
        Assert.Equal(expectedAxis, Uinput.Stick(normalised));
    }

    [Fact]
    public void StickIsClampedAndCentred()
    {
        Assert.Equal(0, Uinput.Stick(0.0));
        Assert.Equal(-32767, Uinput.Stick(-1.0));
        Assert.Equal(32767, Uinput.Stick(1.0));
        Assert.Equal(32767, Uinput.Stick(5.0));
        Assert.Equal(-32767, Uinput.Stick(-5.0));
        Assert.Equal(0, Uinput.Stick(double.NaN));
    }

    [Theory]
    [InlineData(1.0, -32767)]   // up on the phone -> negative evdev value
    [InlineData(0.5, -16384)]
    [InlineData(0.0, 0)]
    [InlineData(-0.5, 16384)]
    [InlineData(-1.0, 32767)]   // down on the phone -> positive evdev value
    public void VerticalStickAxesFlipIntoEvdevsDownPositiveSign(double xinput, int expected)
    {
        // The network layer keeps XInput/vJoy's positive-is-up convention, but
        // Linux evdev ABS_Y and ABS_RY are positive down.
        Assert.Equal(expected, Uinput.StickY(xinput));
        Assert.Equal(Uinput.Stick(0.0 - xinput), Uinput.StickY(xinput));
    }

    [Fact]
    public void PedalsAndTriggersUseTheirFullRange()
    {
        Assert.Equal(0, Uinput.Pedal(0.0));
        Assert.Equal(32767, Uinput.Pedal(1.0));
        Assert.Equal(16384, Uinput.Pedal(0.5));
        Assert.Equal(0, Uinput.Pedal(-1.0));
        Assert.Equal(32767, Uinput.Pedal(2.0));

        Assert.Equal(0, Uinput.Trigger(0.0));
        Assert.Equal(128, Uinput.Trigger(0.5));
        Assert.Equal(255, Uinput.Trigger(1.0));
        Assert.Equal(255, Uinput.Trigger(9.0));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, -1)]   // up
    [InlineData(2, 0, 1)]    // down
    [InlineData(4, -1, 0)]   // left
    [InlineData(8, 1, 0)]    // right
    [InlineData(9, 1, -1)]   // up-right
    [InlineData(6, -1, 1)]   // down-left
    public void DpadBitsBecomeHatAxes(int buttons, int x, int y)
    {
        Assert.Equal((x, y), Uinput.Hat(buttons));
    }

    [Fact]
    public void WheelButtonsAreSixteenAscendingCodes()
    {
        Assert.Equal(16, Uinput.WheelButtons.Length);
        for (int i = 1; i < Uinput.WheelButtons.Length; i++)
            Assert.True(Uinput.WheelButtons[i] > Uinput.WheelButtons[i - 1]);
    }

    [Fact]
    public void PadButtonsFollowTheXpadKeyOrder()
    {
        // SDL's built-in Xbox mapping numbers the keys in ascending code order:
        // A, B, X, Y, LB, RB, back, start, guide, L3, R3.
        Assert.Equal(11, Uinput.PadButtons.Length);
        Assert.Equal(Uinput.BtnSouth, Uinput.PadButtons[0].Code);
        Assert.Equal(Uinput.BtnEast, Uinput.PadButtons[1].Code);
        Assert.Equal(Uinput.BtnNorth, Uinput.PadButtons[2].Code);
        Assert.Equal(Uinput.BtnWest, Uinput.PadButtons[3].Code);
        Assert.Equal(0x130, Uinput.BtnSouth);
        Assert.Equal(0x131, Uinput.BtnEast);
        Assert.Equal(0x133, Uinput.BtnNorth);
        Assert.Equal(0x134, Uinput.BtnWest);

        for (int i = 1; i < Uinput.PadButtons.Length; i++)
            Assert.True(Uinput.PadButtons[i].Code > Uinput.PadButtons[i - 1].Code);
    }

    [Fact]
    public void ViolentPadMaskOnlyPressesKnownButtonsAndHat()
    {
        int mask = 0;
        foreach ((int bit, _) in Uinput.PadButtons)
            mask |= bit;

        // All four d-pad bits set cancel out; a garbage mask must not push a
        // wildly out-of-range hat value into the kernel (AbsHat0X is -1..1).
        Assert.Equal((0, 0), Uinput.Hat(0xFFFF));

        // Every remaining bit above the dpad nibble must be accounted for.
        Assert.Equal(0x1000 | 0x2000 | 0x4000 | 0x8000 | 0x100 | 0x200 | 0x20 | 0x10 | 0x400 | 0x40 | 0x80, mask);
    }

    [Fact]
    public void VirtualDeviceNamesCoverBothPersonas()
    {
        // --report lists only these names in /sys/class/input; the pad keeps the
        // Xbox 360 name, so a "Gyropad" substring test would miss it.
        Assert.True(UinputBridge.IsVirtualDeviceName("Gyropad Wheel"));
        Assert.True(UinputBridge.IsVirtualDeviceName("Microsoft X-Box 360 pad"));
        Assert.True(UinputBridge.IsVirtualDeviceName("microsoft x-box 360 pad"));
        Assert.False(UinputBridge.IsVirtualDeviceName("AT Translated Set 2 keyboard"));
        Assert.False(UinputBridge.IsVirtualDeviceName(""));
    }

    [Fact]
    public void UinputDevicesDegradeQuietlyOffLinux()
    {
        using var bridge = new UinputBridge();
        bridge.Start();

        Assert.False(bridge.WheelReady);
        Assert.False(bridge.PadPlugged);

        if (OperatingSystem.IsLinux())
            return; // a Linux host with a working uinput node would create devices

        Assert.Equal("uinput not available", bridge.Status);
        Assert.Contains("Linux", bridge.Detail);

        bridge.PlugPad();
        Assert.False(bridge.PadPlugged);

        // Feeds must never throw when no device exists: the network thread
        // calls them for every packet.
        bridge.FeedWheel(0.5, 0.5, 0.5, 0.5, 0.5, 0xFFFF);
        bridge.FeedPad(0.5, -0.5, 0.25, -0.25, 1.0, 0.0, 0x1FFF);

        bridge.UnplugPad();
        Assert.Equal("could not create the gamepad", bridge.Status);
    }
}
