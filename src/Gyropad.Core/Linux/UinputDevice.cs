using System.Runtime.InteropServices;

namespace Gyropad.Core.Linux;

/// <summary>
/// One virtual input device created through <c>/dev/uinput</c>.
/// </summary>
/// <remarks>
/// Requires read/write access to <c>/dev/uinput</c>: either the packaged udev
/// rule (<c>TAG+="uaccess"</c>) or membership of a group that owns the node.
/// Creation failures are reported through <paramref name="error"/> instead of
/// throwing so the app can keep running in monitor mode.
/// </remarks>
internal sealed class UinputDevice : IDisposable
{
    private const int OWriteOnly = 1;

    private readonly int _fd;
    private readonly byte[] _buffer = new byte[Uinput.EventSize * 96];
    private int _pending;
    private bool _disposed;

    private UinputDevice(int fd)
    {
        _fd = fd;
    }

    /// <summary>True after a write failed; the owner should drop the device.</summary>
    public bool Failed { get; private set; }

    public static UinputDevice? TryCreate(
        string name,
        ushort vendor,
        ushort product,
        ushort version,
        IReadOnlyList<Uinput.Axis> axes,
        IReadOnlyList<ushort> keys,
        out string error)
    {
        error = "";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            error = "uinput is a Linux kernel interface";
            return null;
        }

        if (IntPtr.Size != 8)
        {
            error = "the uinput ABI in this build assumes a 64-bit kernel";
            return null;
        }

        int fd = SysOpen(Uinput.DevicePath, OWriteOnly);
        if (fd < 0)
        {
            error = DescribeOpenFailure(Marshal.GetLastPInvokeError());
            return null;
        }

        var device = new UinputDevice(fd);
        try
        {
            if (!device.Configure(name, vendor, product, version, axes, keys, out error))
            {
                device.Dispose();
                return null;
            }

            if (!Check(SysIoctl(fd, Uinput.DevCreate)))
            {
                error = "the kernel refused to create the device (" + DescribeErrno(Marshal.GetLastPInvokeError()) + ")";
                device.Dispose();
                return null;
            }

            // Publish a known initial state so clients do not see stale values.
            foreach (Uinput.Axis axis in axes)
                device.PushAbs(axis.Code, 0);
            device.Sync();
            device.Flush();

            return device;
        }
        catch (Exception ex)
        {
            error = "uinput setup failed: " + ex.Message;
            device.Dispose();
            return null;
        }
    }

    private bool Configure(
        string name,
        ushort vendor,
        ushort product,
        ushort version,
        IReadOnlyList<Uinput.Axis> axes,
        IReadOnlyList<ushort> keys,
        out string error)
    {
        error = "";

        if (!Check(SysIoctl(_fd, Uinput.SetEvBit, (int)Uinput.EvSyn))
            || (keys.Count > 0 && !Check(SysIoctl(_fd, Uinput.SetEvBit, (int)Uinput.EvKey)))
            || (axes.Count > 0 && !Check(SysIoctl(_fd, Uinput.SetEvBit, (int)Uinput.EvAbs))))
        {
            error = "could not declare the event types (" + DescribeErrno(Marshal.GetLastPInvokeError()) + ")";
            return false;
        }

        foreach (ushort key in keys)
        {
            int code = key;
            if (!Check(SysIoctl(_fd, Uinput.SetKeyBit, code)))
            {
                error = "could not declare button 0x" + key.ToString("x") + " (" + DescribeErrno(Marshal.GetLastPInvokeError()) + ")";
                return false;
            }
        }

        foreach (Uinput.Axis axis in axes)
        {
            int code = axis.Code;
            if (!Check(SysIoctl(_fd, Uinput.SetAbsBit, code)))
            {
                error = "could not declare axis 0x" + axis.Code.ToString("x") + " (" + DescribeErrno(Marshal.GetLastPInvokeError()) + ")";
                return false;
            }

            var setup = new UinputAbsSetup
            {
                Code = axis.Code,
                Info = new InputAbsInfo
                {
                    Minimum = axis.Min,
                    Maximum = axis.Max
                }
            };
            if (!Check(SysIoctl(_fd, Uinput.AbsSetup, ref setup)))
            {
                error = "could not describe axis 0x" + axis.Code.ToString("x") + " (" + DescribeErrno(Marshal.GetLastPInvokeError()) + ")";
                return false;
            }
        }

        var id = new InputId
        {
            BusType = 0x03, // BUS_USB
            Vendor = vendor,
            Product = product,
            Version = version
        };
        var dev = new UinputSetup
        {
            Id = id,
            EffectsMax = 0,
            Name = name.Length > 79 ? name[..79] : name
        };

        if (!Check(SysIoctl(_fd, Uinput.DevSetup, ref dev)))
        {
            error = "the kernel rejected the device description (" + DescribeErrno(Marshal.GetLastPInvokeError()) + ")";
            return false;
        }

        return true;
    }

    public void PushKey(ushort code, bool pressed)
        => Push(Uinput.EvKey, code, pressed ? 1 : 0);

    public void PushAbs(ushort code, int value)
        => Push(Uinput.EvAbs, code, value);

    public void Sync()
        => Push(Uinput.EvSyn, Uinput.SynReport, 0);

    private void Push(ushort type, ushort code, int value)
    {
        if (_disposed)
            return;

        if (_pending >= 96)
            Flush();

        int at = _pending * Uinput.EventSize;
        _pending++;

        // struct input_event: time is left zero, the kernel stamps it.
        _buffer[at] = 0;
        _buffer[at + 1] = 0;
        _buffer[at + 2] = 0;
        _buffer[at + 3] = 0;
        _buffer[at + 4] = 0;
        _buffer[at + 5] = 0;
        _buffer[at + 6] = 0;
        _buffer[at + 7] = 0;
        _buffer[at + 8] = 0;
        _buffer[at + 9] = 0;
        _buffer[at + 10] = 0;
        _buffer[at + 11] = 0;
        _buffer[at + 12] = 0;
        _buffer[at + 13] = 0;
        _buffer[at + 14] = 0;
        _buffer[at + 15] = 0;

        _buffer[at + 16] = (byte)type;
        _buffer[at + 17] = (byte)(type >> 8);
        _buffer[at + 18] = (byte)code;
        _buffer[at + 19] = (byte)(code >> 8);
        _buffer[at + 20] = (byte)value;
        _buffer[at + 21] = (byte)(value >> 8);
        _buffer[at + 22] = (byte)(value >> 16);
        _buffer[at + 23] = (byte)(value >> 24);
    }

    /// <summary>Hands the queued events to the kernel. Returns false once the device is gone.</summary>
    public bool Flush()
    {
        if (_disposed || Failed || _pending == 0)
            return !Failed;

        int bytes = _pending * Uinput.EventSize;
        _pending = 0;

        nint written = SysWrite(_fd, _buffer, bytes);
        if (written < 0)
        {
            Failed = true;
            return false;
        }

        // A short write only happens when the kernel queue is full; the next
        // report supersedes it, so dropping the remainder is safe.
        return true;
    }

    private static bool Check(int rc) => rc >= 0;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            SysIoctl(_fd, Uinput.DevDestroy);
        }
        catch
        {
            // the node may already be gone
        }

        try
        {
            SysClose(_fd);
        }
        catch
        {
            // nothing else to do
        }
    }

    private static string DescribeOpenFailure(int errno) => errno switch
    {
        2 => "there is no /dev/uinput; load the module with: sudo modprobe uinput",
        13 => "permission denied; install the Gyropad udev rule (see docs/linux-setup.md) or run once as root",
        6 => "no such device; load the module with: sudo modprobe uinput",
        _ => "could not open /dev/uinput (" + DescribeErrno(errno) + ")"
    };

    private static string DescribeErrno(int errno) => errno switch
    {
        1 => "EPERM",
        2 => "ENOENT",
        9 => "EBADF",
        13 => "EACCES",
        16 => "EBUSY",
        19 => "ENODEV",
        22 => "EINVAL",
        6 => "ENXIO",
        _ => "errno " + errno
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct InputId
    {
        public ushort BusType;
        public ushort Vendor;
        public ushort Product;
        public ushort Version;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputAbsInfo
    {
        public int Value;
        public int Minimum;
        public int Maximum;
        public int Fuzz;
        public int Flat;
        public int Resolution;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct UinputSetup
    {
        public InputId Id;
        public uint EffectsMax;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UinputAbsSetup
    {
        public ushort Code;
        public ushort Padding;
        public InputAbsInfo Info;
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "open")]
    private static extern int SysOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", SetLastError = true, EntryPoint = "close")]
    private static extern int SysClose(int fd);

    [DllImport("libc", SetLastError = true, EntryPoint = "write")]
    private static extern nint SysWrite(int fd, byte[] buffer, nint count);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int SysIoctl(int fd, uint request);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int SysIoctl(int fd, uint request, int argument);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int SysIoctl(int fd, uint request, ref UinputSetup argument);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int SysIoctl(int fd, uint request, ref UinputAbsSetup argument);
}
