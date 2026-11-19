using System;
using Gyropad.Core.Transport;

namespace Gyropad.Core.Linux;

/// <summary>
/// Linux replacement for the Windows build's two device bridges:
/// </summary>
/// <remarks>
/// <para>
/// The wheel persona becomes a joystick with vJoy's axis order
/// (X steer, Y throttle, Z brake, Rx clutch, Ry handbrake) and sixteen buttons,
/// so existing game bindings line up with the Windows build.
/// </para>
/// <para>
/// The pad persona becomes an Xbox 360 compatible evdev device. Its key set and
/// order match the kernel's <c>xpad</c> driver, which is what SDL's built-in
/// gamepad mapping expects, so Steam and Proton titles see a normal controller.
/// </para>
/// <para>
/// One deliberate difference: on Windows the "wheel input, but the game wants a
/// pad" path fed vJoy's pad layout. Here it feeds the pad device, because a
/// Linux title that needs a controller is looking for a gamepad, not a joystick.
/// </para>
/// </remarks>
public sealed class UinputBridge : IOutputDevice, IDisposable
{
    private const ushort WheelVendor = 0x1209;
    private const ushort WheelProduct = 0x0001;
    private const ushort WheelVersion = 0x0001;
    private const string WheelName = "Gyropad Wheel";

    // The wired Xbox 360 ids. SDL ships a mapping for this GUID, so no
    // per-distribution configuration is needed.
    private const ushort PadVendor = 0x045e;
    private const ushort PadProduct = 0x028e;
    private const ushort PadVersion = 0x0110;
    private const string PadName = "Microsoft X-Box 360 pad";

    /// <summary>
    /// True for the kernel names this bridge gives its own devices. The pad
    /// persona deliberately uses the Xbox 360 name, so diagnostics cannot just
    /// look for "Gyropad" in the name.
    /// </summary>
    public static bool IsVirtualDeviceName(string name) =>
        string.Equals(name, WheelName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, PadName, StringComparison.OrdinalIgnoreCase);

    private static readonly Uinput.Axis[] WheelAxes =
    [
        new(Uinput.AbsX, -32767, 32767),
        new(Uinput.AbsY, 0, 32767),
        new(Uinput.AbsZ, 0, 32767),
        new(Uinput.AbsRx, 0, 32767),
        new(Uinput.AbsRy, 0, 32767)
    ];

    private static readonly Uinput.Axis[] PadAxes =
    [
        new(Uinput.AbsX, -32767, 32767),
        new(Uinput.AbsY, -32767, 32767),
        new(Uinput.AbsRx, -32767, 32767),
        new(Uinput.AbsRy, -32767, 32767),
        new(Uinput.AbsZ, 0, 255),
        new(Uinput.AbsRz, 0, 255),
        new(Uinput.AbsHat0X, -1, 1),
        new(Uinput.AbsHat0Y, -1, 1)
    ];

    private static readonly ushort[] PadKeys =
    [
        Uinput.BtnSouth,
        Uinput.BtnEast,
        Uinput.BtnNorth,
        Uinput.BtnWest,
        Uinput.BtnTL,
        Uinput.BtnTR,
        Uinput.BtnSelect,
        Uinput.BtnStart,
        Uinput.BtnMode,
        Uinput.BtnThumbL,
        Uinput.BtnThumbR
    ];

    private readonly object _gate = new();
    private UinputDevice? _wheel;
    private UinputDevice? _pad;
    private bool _started;
    private string _status = "not started";
    private string _detail = "";

    /// <summary>Human-readable state for the window, mirroring the vJoy status.</summary>
    public string Status
    {
        get
        {
            lock (_gate)
                return _status;
        }
    }

    /// <summary>Extra explanation for <see cref="Status"/>; may be empty.</summary>
    public string Detail
    {
        get
        {
            lock (_gate)
                return _detail;
        }
    }

    public bool WheelReady
    {
        get
        {
            lock (_gate)
                return _wheel is { Failed: false };
        }
    }

    public bool PadPlugged
    {
        get
        {
            lock (_gate)
                return _pad is { Failed: false };
        }
    }

    /// <summary>Creates the wheel device. Safe to call twice.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started)
                return;
            _started = true;

            // The pad is created first on purpose. Linux numbers input devices
            // in creation order, and a title that picks "controller 1" or scans
            // /dev/input in order then finds the Xbox pad before the wheel.
            PlugPad();

            _wheel = UinputDevice.TryCreate(
                WheelName, WheelVendor, WheelProduct, WheelVersion,
                WheelAxes, Uinput.WheelButtons, out string error);

            if (_wheel != null)
            {
                _status = "ready";
                _detail = "";
            }
            else
            {
                _status = "uinput not available";
                _detail = error;
            }
        }
    }

    /// <summary>
    /// Creates both personas and destroys them again, so <c>--doctor</c> can
    /// prove the kernel accepts their descriptions without a game running.
    /// Each string is empty when the device was created successfully, or the
    /// reason it was not.
    /// </summary>
    public static (string Wheel, string Pad) Probe()
    {
        string wheel = ProbeOne(WheelName, WheelVendor, WheelProduct, WheelVersion, WheelAxes, Uinput.WheelButtons);
        string pad = ProbeOne(PadName, PadVendor, PadProduct, PadVersion, PadAxes, PadKeys);
        return (wheel, pad);
    }

    private static string ProbeOne(
        string name,
        ushort vendor,
        ushort product,
        ushort version,
        Uinput.Axis[] axes,
        ushort[] keys)
    {
        UinputDevice? device = UinputDevice.TryCreate(
            name, vendor, product, version, axes, keys, out string error);
        if (device == null)
            return error;

        device.Dispose();
        return "";
    }

    public void PlugPad()
    {
        lock (_gate)
        {
            if (_pad != null && !_pad.Failed)
                return;

            _pad?.Dispose();
            _pad = UinputDevice.TryCreate(
                PadName, PadVendor, PadProduct, PadVersion,
                PadAxes, PadKeys, out string error);

            if (_pad == null)
            {
                _status = "could not create the gamepad";
                _detail = error;
            }
        }
    }

    public void UnplugPad()
    {
        lock (_gate)
        {
            _pad?.Dispose();
            _pad = null;
        }
    }

    public void FeedWheel(double steer, double throttle, double brake, double clutch, double handbrake, int buttons)
    {
        lock (_gate)
        {
            UinputDevice? device = _wheel;
            if (device == null || device.Failed)
                return;

            device.PushAbs(Uinput.AbsX, Uinput.Stick(steer));
            device.PushAbs(Uinput.AbsY, Uinput.Pedal(throttle));
            device.PushAbs(Uinput.AbsZ, Uinput.Pedal(brake));
            device.PushAbs(Uinput.AbsRx, Uinput.Pedal(clutch));
            device.PushAbs(Uinput.AbsRy, Uinput.Pedal(handbrake));

            for (int i = 0; i < Uinput.WheelButtons.Length; i++)
                device.PushKey(Uinput.WheelButtons[i], (buttons & (1 << i)) != 0);

            device.Sync();
            if (!device.Flush())
                DropWheel(device);
        }
    }

    public void FeedPad(double lx, double ly, double rx, double ry, double lt, double rt, int buttons)
    {
        lock (_gate)
        {
            UinputDevice? device = _pad;
            if (device == null || device.Failed)
                return;

            // Callers use the Windows/XInput sign (positive is up); evdev's
            // ABS_Y and ABS_RY are positive down, so the vertical axes flip here.
            device.PushAbs(Uinput.AbsX, Uinput.Stick(lx));
            device.PushAbs(Uinput.AbsY, Uinput.StickY(ly));
            device.PushAbs(Uinput.AbsRx, Uinput.Stick(rx));
            device.PushAbs(Uinput.AbsRy, Uinput.StickY(ry));
            device.PushAbs(Uinput.AbsZ, Uinput.Trigger(lt));
            device.PushAbs(Uinput.AbsRz, Uinput.Trigger(rt));

            (int hatX, int hatY) = Uinput.Hat(buttons);
            device.PushAbs(Uinput.AbsHat0X, hatX);
            device.PushAbs(Uinput.AbsHat0Y, hatY);

            foreach ((int bit, ushort code) in Uinput.PadButtons)
                device.PushKey(code, (buttons & bit) != 0);

            device.Sync();
            if (!device.Flush())
                DropPad(device);
        }
    }

    private void DropWheel(UinputDevice device)
    {
        if (!ReferenceEquals(_wheel, device))
            return;

        _wheel = null;
        device.Dispose();
        _status = "the wheel device disappeared";
        _detail = "reconnect it or restart Gyropad";
    }

    private void DropPad(UinputDevice device)
    {
        if (!ReferenceEquals(_pad, device))
            return;

        _pad = null;
        device.Dispose();
        _status = "the gamepad disappeared";
        _detail = "reconnect it or restart Gyropad";
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _pad?.Dispose();
            _pad = null;
            _wheel?.Dispose();
            _wheel = null;
            _started = false;
        }
    }
}
