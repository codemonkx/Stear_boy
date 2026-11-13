using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Gyropad.Core.Config;
using Gyropad.Core.Input;
using Gyropad.Core.Protocol;
using Gyropad.Core.Telemetry;
using Gyropad.Core.Titles;

namespace Gyropad.Core.Transport;

/// <summary>
/// The one platform-specific dependency of the network layer: where decoded
/// input goes. On Linux this is the uinput bridge; tests use a recording fake.
/// </summary>
public interface IOutputDevice
{
    /// <summary>True when the wheel persona (vJoy equivalent) is available.</summary>
    bool WheelReady { get; }

    /// <summary>True while the pad persona (Xbox equivalent) is plugged in.</summary>
    bool PadPlugged { get; }

    void PlugPad();

    void UnplugPad();

    /// <summary>Steer and pedals are normalised to -1..1 and 0..1.</summary>
    void FeedWheel(double steer, double throttle, double brake, double clutch, double handbrake, int buttons);

    /// <summary>
    /// All axes are normalised, triggers are 0..1, and the button mask uses the
    /// Xbox 360 layout the original ViGEm path produced (D-pad included).
    /// </summary>
    void FeedPad(double lx, double ly, double rx, double ry, double lt, double rt, int buttons);
}

/// <summary>
/// Phone link: discovery, pairing, input decoding, routing and telemetry.
/// Ported from the Windows build with the two device bridges replaced by
/// <see cref="IOutputDevice"/> and the port-owner lookup swapped for /proc.
/// </summary>
public sealed class Net
{
    private const int ShiftMs = 120;
    private const int LinkDeadMs = 2000;
    private const int RoamQuietMs = 1500;

    private readonly Store _store;
    private readonly Wheel _w;
    private readonly Pad _p;
    private readonly OutGauge _og;
    private readonly OutSim? _sim;
    private readonly DriftAssist? _assist;
    private readonly string _myName;

    public readonly List<Discovered> Devices = [];

    public volatile bool Connected;
    public volatile bool PadMode;
    public volatile bool UsingPad;
    public volatile bool WheelViaPad;
    public volatile bool PadBecauseGame;

    public GameProfile ActiveProfile = Games.Generic;

    public volatile string StatusLine = "Open Gyropad on your phone and tap Connect";
    public volatile int SettingsAt;
    public volatile int DeadButtons;
    public volatile int PedalCaps;
    public volatile int PedalLabels;
    public volatile int PedalLabels2;
    public volatile bool CamStickWanted;
    public volatile int HoldWhat;
    public volatile int HoldAt;
    public float HoldProgress;

    public Func<string, bool>? OnApprovalRequest;
    public Action? OnChanged;
    public Action<Discovered>? OnPairWanted;

    private volatile bool _run;
    private UdpClient? _input;
    private UdpClient? _beacon;
    private TcpListener? _pairServer;
    private byte[] _token = [];
    private IPAddress? _peer;
    private volatile int _lastSeq = -1;
    private ushort _pendingButtons;
    private volatile uint _echoTMs;
    private int _lastGear = -99;
    private volatile int _pendingHaptic;
    private volatile int _pokeRepeats;
    private int _shiftUntil;
    private int _shiftDir;
    private bool _lastShiftUp;
    private bool _lastShiftDown;
    private int _usbCheckedAt;
    private bool _usbCached;

    public volatile int PairPort;
    public volatile int InputPort;
    public volatile string InputFault = "";
    public volatile string InputNote = "";
    public volatile string PairError = "";

    private int _lastGoodRx;
    private volatile IPEndPoint? _telemTo;

    public volatile int FaceMode = 1;
    public volatile bool FaceSeen;
    public readonly int[] FacePad = [0, 1, 2, 3];
    public readonly int[] FaceSys = [0, 1, 2];
    public volatile bool FaceTopSwapped;
    public volatile bool FaceClusterSwapped;
    public volatile bool FaceWheelHeld;
    public readonly float[] FaceGamma = [1f, 1f, 1f, 1f];
    public readonly float[] FaceOff = new float[11];
    public volatile bool FaceClusterLinked;
    public volatile bool FaceStickLinked;
    public volatile bool FaceOffSeen;
    public volatile float FaceCurveK;
    public volatile int FaceDegPerUnit;
    public volatile float FaceBacklashDeg;

    public Action? OnFaceChanged;

    public Net(Store s, Wheel w, Pad p, IOutputDevice output, OutGauge og, OutSim? sim, DriftAssist? assist, string myName)
    {
        _store = s;
        _w = w;
        _p = p;
        Output = output;
        _og = og;
        _sim = sim;
        _assist = assist;
        _myName = myName;
    }

    public IOutputDevice Output { get; }

    public string Status
    {
        get
        {
            if (InputFault.Length > 0)
                return InputFault;
            if (!Connected && InputNote.Length > 0)
                return InputNote;
            return StatusLine;
        }
    }

    public bool HoldLive => HoldWhat != 0 && Environment.TickCount - HoldAt < 250;

    public bool PeerOverUsb
    {
        get
        {
            IPAddress? peer = _peer;
            if (peer == null)
                return false;
            if (Environment.TickCount - _usbCheckedAt < 2000)
                return _usbCached;

            _usbCheckedAt = Environment.TickCount;
            _usbCached = false;
            try
            {
                byte[] peerBytes = peer.GetAddressBytes();
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || !LooksLikeTether(ni))
                        continue;

                    foreach (UnicastIPAddressInformation unicast in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null)
                            continue;

                        byte[] localBytes = unicast.Address.GetAddressBytes();
                        byte[] maskBytes = unicast.IPv4Mask.GetAddressBytes();
                        bool sameSubnet = true;
                        for (int i = 0; i < 4; i++)
                        {
                            if (!sameSubnet)
                                break;
                            if ((localBytes[i] & maskBytes[i]) != (peerBytes[i] & maskBytes[i]))
                                sameSubnet = false;
                        }

                        if (sameSubnet)
                        {
                            _usbCached = true;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // interface information is best effort
            }

            return false;
        }
    }

    /// <summary>
    /// A dual-mode listener reports IPv4 peers as ::ffff:a.b.c.d. Normalising
    /// keeps the stored host readable and lets subnet/tether checks use IPv4.
    /// </summary>
    private static IPAddress Unmap(IPAddress address)
        => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static float Clamp(float v, float lo, float hi)
    {
        if (v < lo)
            return lo;
        if (v > hi)
            return hi;
        return v;
    }

    public bool WheelOnTrial(GameProfile? g)
    {
        if (g == null)
            return false;
        if (_store.WheelTrialUntil == 0)
            return false;
        if (Environment.TickCount - _store.WheelTrialUntil >= 0)
            return false;
        return string.Equals(_store.WheelTrialExe, g.Exe, StringComparison.OrdinalIgnoreCase);
    }

    public bool WheelKept(GameProfile? g)
    {
        if (g == null || !_store.ForceWheel)
            return false;
        return string.Equals(_store.ForceWheelExe, g.Exe, StringComparison.OrdinalIgnoreCase);
    }

    public bool WheelForced(GameProfile? g) => WheelOnTrial(g) || WheelKept(g);

    private void Route(bool isPad)
    {
        GameProfile current = Games.Current;
        bool wheelMissing = !Output.WheelReady;
        bool forced = WheelForced(current);
        bool usePad = isPad || wheelMissing || (current.NeedsPad && !forced);

        WheelViaPad = !isPad && usePad;
        PadBecauseGame = !isPad && !wheelMissing && current.NeedsPad && !forced;
        ActiveProfile = current;
        UsingPad = usePad;

        if (usePad)
        {
            if (!Output.PadPlugged)
                Output.PlugPad();

            if (isPad)
                FeedPadFace();
            else
                FeedWheelAsPad(current.PadFallback ?? current);
            return;
        }

        if (Output.PadPlugged)
            Output.UnplugPad();

        DeadButtons = 0;
        PedalCaps = 0;

        double steer = _w.Steer;
        if (_assist != null)
            steer = _assist.Apply(steer, _sim);
        _w.SteerOut = steer;
        Output.FeedWheel(steer, _w.Thr, _w.Brk, _w.Clu, _w.Hb, _w.Buttons);
    }

    private void FeedPadFace()
    {
        if (!Output.PadPlugged)
            return;

        DeadButtons = 0;
        PedalCaps = 0;
        PedalLabels = 0;
        PedalLabels2 = 0;
        CamStickWanted = false;

        int buttons = 0;
        if (PadBit(1))
            buttons |= 0x1000; // A
        if (PadBit(2))
            buttons |= 0x2000; // B
        if (PadBit(4))
            buttons |= 0x4000; // X
        if (PadBit(5))
            buttons |= 0x8000; // Y
        if (PadBit(7))
            buttons |= 0x100;  // LB
        if (PadBit(8))
            buttons |= 0x200;  // RB
        if (PadBit(11))
            buttons |= 0x20;   // back
        if (PadBit(12))
            buttons |= 0x10;   // start
        if (PadBit(13))
            buttons |= 0x400;  // guide
        if (PadBit(14))
            buttons |= 0x40;   // L3
        if (PadBit(15))
            buttons |= 0x80;   // R3
        buttons |= HatBits(_p.Hat);

        Output.FeedPad(_p.Lx, 0.0 - _p.Ly, _p.Rx, 0.0 - _p.Ry, _p.Lt, _p.Rt, buttons);
    }

    private bool PadBit(int button) => (_p.Buttons & (1 << (button - 1))) != 0;

    private static int HatBits(int hat) => hat switch
    {
        0 => 1,
        1 => 9,
        2 => 8,
        3 => 10,
        4 => 2,
        5 => 6,
        6 => 4,
        7 => 5,
        _ => 0
    };

    private void FeedWheelAsPad(GameProfile g)
    {
        if (!Output.PadPlugged)
            return;

        double steer = _w.Steer;
        if (_assist != null)
            steer = _assist.Apply(steer, _sim);
        _w.SteerOut = steer;

        if (g.SteerAntiDeadzone > 0.0001 && Math.Abs(steer) > 0.0001)
        {
            double adz = g.SteerAntiDeadzone;
            steer = Math.Sign(steer) * (adz + (1.0 - adz) * Math.Abs(steer));
            if (steer > 1.0)
                steer = 1.0;
            if (steer < -1.0)
                steer = -1.0;
        }

        int taken = 0;
        taken |= g.Camera;
        if (g.HasHorn)
            taken |= g.Horn;
        if (g.HasLights)
            taken |= g.Lights;
        if (g.HasMap)
            taken |= g.Map;
        if (g.HasIgnition)
            taken |= g.Ignition;
        if (g.HasGears)
        {
            taken |= g.GearUp;
            taken |= g.GearDown;
        }
        if (g.HasHandbrake)
            taken |= g.Handbrake;
        if (g.HasClutch)
            taken |= g.Clutch;
        if (g.DigitalBrake)
            taken |= g.BrakeButton;
        if (g.DigitalThrottle)
            taken |= g.ThrottleButton;

        int dead = 0;
        int buttons = 0;

        if (g.HasHandbrake && _w.Hb > 0.5)
            buttons |= g.Handbrake;
        if (g.HasClutch && _w.Clu > 0.5)
            buttons |= g.Clutch;

        if (g.HasGears)
        {
            if (g.ShiftWithStick)
            {
                bool up = WheelBit(0);
                bool down = WheelBit(1);
                if (up && !_lastShiftUp)
                {
                    _shiftUntil = Environment.TickCount + ShiftMs;
                    _shiftDir = 1;
                }
                if (down && !_lastShiftDown)
                {
                    _shiftUntil = Environment.TickCount + ShiftMs;
                    _shiftDir = -1;
                }
                _lastShiftUp = up;
                _lastShiftDown = down;
            }
            else
            {
                if (WheelBit(0))
                    buttons |= g.GearUp;
                if (WheelBit(1))
                    buttons |= g.GearDown;
            }
        }
        else
        {
            dead |= 3;
        }

        if (WheelBit(5))
            buttons |= g.Camera;

        if (g.HasHorn)
        {
            if (WheelBit(4))
                buttons |= g.Horn;
        }
        else
        {
            dead |= 0x10;
        }

        if (g.HasLights)
        {
            if (WheelBit(3))
                buttons |= g.Lights;
        }
        else
        {
            dead |= 8;
        }

        if (g.HasMap)
        {
            if (WheelBit(7))
                buttons |= g.Map;
        }
        else
        {
            dead |= 0x80;
        }

        if (g.HasIgnition)
        {
            if (WheelBit(2))
                buttons |= g.Ignition;
        }
        else
        {
            dead |= 4;
        }

        if (g.DigitalBrake && _w.Brk >= g.DigitalAt)
            buttons |= g.BrakeButton;
        if (g.DigitalThrottle && _w.Thr >= g.DigitalAt)
            buttons |= g.ThrottleButton;

        int faceTaken = g.ShareFaceButtons ? 0 : taken;
        faceTaken |= Generic(ref buttons, ref dead, faceTaken, g.FaceY != 0 ? g.FaceY : (ushort)32768, 12);
        faceTaken |= Generic(ref buttons, ref dead, faceTaken, g.FaceX != 0 ? g.FaceX : (ushort)16384, 13);
        faceTaken |= Generic(ref buttons, ref dead, faceTaken, g.FaceA != 0 ? g.FaceA : (ushort)4096, 14);
        faceTaken |= Generic(ref buttons, ref dead, faceTaken, g.FaceB != 0 ? g.FaceB : (ushort)8192, 15);
        taken |= faceTaken;

        double lookX = 0.0;
        double lookY = 0.0;
        if (g.CamIsStick)
        {
            lookX = _w.LookX;
            lookY = _w.LookY;
        }

        if (g.ShiftWithStick && Environment.TickCount < _shiftUntil)
        {
            lookY = _shiftDir;
            buttons |= g.ShiftHold;
        }

        if (WheelBit(9))
        {
            if (g.DpadDrivesStick)
                lookX--;
            buttons |= Free(taken, 4);
        }
        if (WheelBit(11))
        {
            if (g.DpadDrivesStick)
                lookX++;
            buttons |= Free(taken, 8);
        }
        if (WheelBit(8))
        {
            if (g.DpadDrivesStick)
                lookY++;
            buttons |= Free(taken, 1);
        }
        if (WheelBit(10))
        {
            if (g.DpadDrivesStick)
                lookY--;
            buttons |= Free(taken, 2);
        }

        DeadButtons = dead;
        PedalCaps = Caps(g);
        PedalLabels = Wire.PedalLabels(g.ClutchLabel, g.HandbrakeLabel);
        PedalLabels2 = Wire.PedalLabels2(g.ThrottleLabel, g.BrakeLabel);
        CamStickWanted = g.CamIsStick;

        bool brakeIsAxis = g.BrakeAxisFromHandbrake || !g.DigitalBrake;
        double brake;
        if (g.BrakeAxisFromHandbrake)
            brake = _w.Hb;
        else if (g.DigitalBrake)
            brake = _w.Brk >= g.DigitalAt ? 1.0 : 0.0;
        else
            brake = _w.Brk;

        double throttle;
        if (g.DigitalThrottle)
            throttle = _w.Thr >= g.DigitalAt ? 1.0 : 0.0;
        else
            throttle = _w.Thr;

        if (!g.DigitalThrottle && g.ThrottleAntiDeadzone > 0.0001 && throttle > 0.0001)
        {
            throttle = g.ThrottleAntiDeadzone + (1.0 - g.ThrottleAntiDeadzone) * throttle;
            if (throttle > 1.0)
                throttle = 1.0;
        }

        if (brakeIsAxis && g.BrakeAntiDeadzone > 0.0001 && brake > 0.0001)
        {
            brake = g.BrakeAntiDeadzone + (1.0 - g.BrakeAntiDeadzone) * brake;
            if (brake > 1.0)
                brake = 1.0;
        }

        Output.FeedPad(steer, 0.0, lookX, lookY, brake, throttle, buttons);
    }

    private ushort Generic(ref int b, ref int dead, int taken, ushort button, int bit)
    {
        if ((taken & button) != 0)
        {
            dead |= 1 << bit;
            return 0;
        }

        if (WheelBit(bit))
            b |= button;
        return button;
    }

    private static int Free(int taken, int button) => (taken & button) == 0 ? button : 0;

    private static int Caps(GameProfile g)
    {
        int caps = 0;
        if (!g.HasClutch)
            caps |= Wire.PedAbsent(2);
        if (!g.HasHandbrake)
            caps |= Wire.PedAbsent(3);
        if (g.HasClutch && g.DigitalClutch)
            caps |= Wire.PedDigital(2);
        if (g.HasHandbrake && g.DigitalHandbrake)
            caps |= Wire.PedDigital(3);
        if (g.DigitalThrottle)
            caps |= Wire.PedDigital(0);
        if (g.DigitalBrake)
            caps |= Wire.PedDigital(1);
        return caps;
    }

    private bool WheelBit(int bit) => (_w.Buttons & (1 << bit)) != 0;

    public void Start()
    {
        if (_run)
            return;

        _run = true;
        AdoptStoredPeer();
        StartThread(DiscoveryLoop, "gyropad-discovery");
        StartThread(PairServerLoop, "gyropad-pairserver");
        StartThread(InputLoop, "gyropad-input", hot: true);
        StartThread(TelemetryLoop, "gyropad-telemetry");
    }

    private static void StartThread(ThreadStart fn, string name, bool hot = false)
    {
        var thread = new Thread(fn) { IsBackground = true, Name = name };
        if (hot)
        {
            try
            {
                thread.Priority = ThreadPriority.AboveNormal;
            }
            catch
            {
                // not permitted on some platforms
            }
        }

        thread.Start();
    }

    public void Stop()
    {
        _run = false;
        try
        {
            _input?.Close();
        }
        catch
        {
            // shutting down
        }
        try
        {
            _beacon?.Close();
        }
        catch
        {
            // shutting down
        }
        try
        {
            _pairServer?.Stop();
        }
        catch
        {
            // shutting down
        }
    }

    private void AdoptStoredPeer()
    {
        if (!_store.HasPeer)
        {
            StatusLine = "Open Gyropad on your phone and tap Connect. Both devices must be on the same Wi-Fi.";
            return;
        }

        try
        {
            _peer = Unmap(IPAddress.Parse(_store.PeerHost));
            _token = Encoding.ASCII.GetBytes(_store.PeerToken);
            _w.Rejected = 0;
            StatusLine = "Not connected - open Gyropad on your phone";
        }
        catch
        {
            // a malformed stored address leaves the app unpaired
        }
    }

    private static bool LooksLikeTether(NetworkInterface ni)
    {
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            return false;
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            return false;

        string name = ni.Name ?? "";
        if (name.StartsWith("usb", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("enx", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("rndis", StringComparison.OrdinalIgnoreCase))
            return true;

        string text = ((ni.Description ?? "") + " " + name).ToUpperInvariant();
        if (text.Contains("REMOTE NDIS"))
            return true;
        if (text.Contains("RNDIS"))
            return true;
        if (text.Contains("NCM"))
            return true;
        if (text.Contains("APPLE MOBILE"))
            return true;
        if (text.Contains("TETHER") || text.Contains("ANDROID"))
            return true;
        if (text.Contains("USB"))
            return true;
        return false;
    }

    private static List<IPEndPoint> BroadcastTargets()
    {
        var targets = new List<IPEndPoint> { new(IPAddress.Broadcast, 5006) };

        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up
                    || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (UnicastIPAddressInformation unicast in ni.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null)
                        continue;

                    byte[] addressBytes = unicast.Address.GetAddressBytes();
                    byte[] maskBytes = unicast.IPv4Mask.GetAddressBytes();

                    bool hasHostBits = false;
                    for (int i = 0; i < 4; i++)
                    {
                        if (maskBytes[i] != byte.MaxValue)
                            hasHostBits = true;
                    }

                    if (!hasHostBits)
                        continue;

                    var bcastBytes = new byte[4];
                    for (int i = 0; i < 4; i++)
                        bcastBytes[i] = (byte)(addressBytes[i] | (byte)~maskBytes[i]);

                    var bcast = new IPAddress(bcastBytes);
                    if (!targets.Exists(e => e.Address.Equals(bcast)))
                        targets.Add(new IPEndPoint(bcast, 5006));
                }
            }
        }
        catch
        {
            // fall back to the limited broadcast address
        }

        return targets;
    }

    public static string PeerInterface(string peerHost)
    {
        if (string.IsNullOrEmpty(peerHost))
            return "";

        try
        {
            if (!IPAddress.TryParse(peerHost, out IPAddress? address))
                return "";
            if (IPAddress.IsLoopback(address))
                return "USB (adb)";

            byte[] peerBytes = address.GetAddressBytes();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                foreach (UnicastIPAddressInformation unicast in ni.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != address.AddressFamily)
                        continue;

                    byte[] localBytes = unicast.Address.GetAddressBytes();
                    if (localBytes.Length != peerBytes.Length)
                        continue;

                    byte[]? mask = null;
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        if (unicast.IPv4Mask == null)
                            continue;
                        mask = unicast.IPv4Mask.GetAddressBytes();
                    }

                    if (mask == null)
                    {
                        bool same = true;
                        for (int i = 0; i < 8; i++)
                        {
                            if (!same)
                                break;
                            if (localBytes[i] != peerBytes[i])
                                same = false;
                        }

                        if (same)
                            return ni.Name;
                        continue;
                    }

                    bool sameSubnet = true;
                    for (int i = 0; i < mask.Length; i++)
                    {
                        if (!sameSubnet)
                            break;
                        if ((localBytes[i] & mask[i]) != (peerBytes[i] & mask[i]))
                            sameSubnet = false;
                    }

                    if (sameSubnet)
                        return ni.Name;
                }
            }
        }
        catch
        {
            // interface lookup is advisory only
        }

        return "";
    }

    private static string BestV6()
    {
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up
                    || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (UnicastIPAddressInformation unicast in ni.GetIPProperties().UnicastAddresses)
                {
                    IPAddress address = unicast.Address;
                    if (address.AddressFamily == AddressFamily.InterNetworkV6
                        && !address.IsIPv6LinkLocal
                        && !address.IsIPv6SiteLocal
                        && !address.IsIPv6Multicast
                        && !IPAddress.IPv6Loopback.Equals(address))
                        return address.ToString();
                }
            }
        }
        catch
        {
            // optional
        }

        return "";
    }

    private void DiscoveryLoop()
    {
        try
        {
            _beacon = new UdpClient();
            _beacon.EnableBroadcast = true;
            _beacon.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _beacon.Client.Bind(new IPEndPoint(IPAddress.Any, Wire.PortDiscovery));
            _beacon.Client.ReceiveTimeout = 600;
        }
        catch
        {
            return;
        }

        byte[] announce = [];
        int lastPair = -1;
        int lastInput = -1;
        int lastAnnounceAt = 0;
        int lastTargetsAt = 0;
        List<IPEndPoint> targets = [];

        while (_run)
        {
            if (Environment.TickCount - lastTargetsAt > 4000 || targets.Count == 0)
            {
                lastTargetsAt = Environment.TickCount;
                targets = BroadcastTargets();
            }

            if (Environment.TickCount - lastAnnounceAt > 2000)
            {
                lastAnnounceAt = Environment.TickCount;
                int pairPort = PairPort;
                int inputPort = InputPort;
                if (pairPort > 0 && inputPort > 0)
                {
                    if (pairPort != lastPair || inputPort != lastInput)
                    {
                        lastPair = pairPort;
                        lastInput = inputPort;
                        string v6 = BestV6();
                        announce = Encoding.ASCII.GetBytes(
                            "PW2 ANNOUNCE PC " + _myName + " " + pairPort
                            + (v6.Length > 0 ? " " + v6 : "")
                            + (inputPort != Wire.PortInput ? " in=" + inputPort : ""));
                    }

                    foreach (IPEndPoint target in targets)
                    {
                        try
                        {
                            _beacon.Send(announce, announce.Length, target);
                        }
                        catch
                        {
                            // one bad interface must not stop discovery
                        }
                    }
                }
            }

            try
            {
                var remote = new IPEndPoint(IPAddress.Any, Wire.PortDiscovery);
                byte[] bytes = _beacon.Receive(ref remote);
                string text = Encoding.ASCII.GetString(bytes).Trim();
                string[] parts = text.Split(' ');

                if (parts.Length >= 5 && parts[0] == "PW2" && parts[1] == "ANNOUNCE" && parts[2] == "PHONE")
                {
                    if (!int.TryParse(parts[4], out int pairPort))
                        pairPort = Wire.PortPairPhone;
                    Remember(parts[3], remote.Address.ToString(), pairPort);
                }
                else if (parts.Length >= 3 && parts[0] == "PW2" && parts[1] == "PAIRME")
                {
                    // Phone sends "PW2 PAIRME <name> <pairPort> [<ipv6>]". The
                    // Windows build read the name from index 3, which only works if
                    // the phone inserts a token; accept both shapes.
                    int nameAt = 2;
                    int portAt = 3;
                    if (parts.Length >= 5 && int.TryParse(parts[4], out _))
                    {
                        nameAt = 3;
                        portAt = 4;
                    }

                    if (parts.Length > nameAt)
                    {
                        int pairPort = Wire.PortPairPhone;
                        if (portAt < parts.Length && int.TryParse(parts[portAt], out int parsedPort))
                            pairPort = parsedPort;

                        string host = remote.Address.ToString();
                        string phoneName = parts[nameAt];
                        Remember(phoneName, host, pairPort);
                        OnPairWanted?.Invoke(new Discovered
                        {
                            Name = phoneName,
                            Host = host,
                            PairPort = pairPort,
                            SeenAt = Environment.TickCount
                        });
                    }
                }
            }
            catch
            {
                // receive timeout or shutdown
            }

            Expire();
        }
    }

    private void Remember(string name, string host, int port)
    {
        bool added = false;
        lock (Devices)
        {
            Discovered? existing = null;
            foreach (Discovered device in Devices)
            {
                if (device.Host == host)
                {
                    existing = device;
                    break;
                }
            }

            if (existing == null)
            {
                Devices.Add(new Discovered
                {
                    Name = name,
                    Host = host,
                    PairPort = port,
                    SeenAt = Environment.TickCount
                });
                added = true;
            }
            else
            {
                existing.SeenAt = Environment.TickCount;
                existing.Name = name;
            }
        }

        if (added)
            OnChanged?.Invoke();
    }

    private void Expire()
    {
        bool removed = false;
        lock (Devices)
        {
            for (int i = Devices.Count - 1; i >= 0; i--)
            {
                if (Environment.TickCount - Devices[i].SeenAt > 9000)
                {
                    Devices.RemoveAt(i);
                    removed = true;
                }
            }
        }

        if (removed)
            OnChanged?.Invoke();
    }

    public Discovered[] Snapshot()
    {
        lock (Devices)
        {
            return [.. Devices];
        }
    }

    private void PairServerLoop()
    {
        for (int port = Wire.PortPair; port < Wire.PortPair + 5; port++)
        {
            try
            {
                try
                {
                    _pairServer = new TcpListener(IPAddress.IPv6Any, port);
                    _pairServer.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                    // Lets the app restart immediately after a pairing connection
                    // that is still in TIME_WAIT (common on Linux).
                    _pairServer.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _pairServer.Start();
                }
                catch
                {
                    _pairServer = new TcpListener(IPAddress.Any, port);
                    _pairServer.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _pairServer.Start();
                }

                PairPort = port;
                PairError = "";
            }
            catch
            {
                _pairServer = null;
                continue;
            }

            break;
        }

        if (_pairServer == null)
        {
            PairPort = 0;
            PairError = "Gyropad could not open a port to accept your phone. Another program is probably using it. Closing that program, or restarting this PC, will clear it.";
            OnChanged?.Invoke();
            return;
        }

        while (_run)
        {
            try
            {
                TcpClient client = _pairServer.AcceptTcpClient();
                var thread = new Thread(() => HandleInbound(client)) { IsBackground = true };
                thread.Start();
            }
            catch
            {
                if (!_run)
                    break;
            }
        }
    }

    private void HandleInbound(TcpClient c)
    {
        try
        {
            c.ReceiveTimeout = 60000;
            NetworkStream stream = c.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII);
            var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\n", AutoFlush = true };

            string? line = reader.ReadLine();
            if (line == null)
                return;

            string[] parts = line.Trim().Split(' ');
            string peerHost = Unmap(((IPEndPoint)c.Client.RemoteEndPoint!).Address).ToString();

            if (parts.Length >= 2 && parts[0] == "PAIR")
            {
                string phoneName = string.Join(" ", parts, 1, parts.Length - 1);
                if (OnApprovalRequest == null || !OnApprovalRequest(phoneName))
                {
                    writer.WriteLine("NO");
                    return;
                }

                string token = NewToken();
                _store.PeerName = phoneName;
                _store.PeerHost = peerHost;
                _store.PeerToken = token;
                _store.Save();
                AdoptStoredPeer();
                writer.WriteLine("OK " + token);
                Connected = true;
                OnChanged?.Invoke();
            }
            else
            {
                if (parts.Length < 2 || parts[0] != "HELLO")
                    return;

                if (_store.HasPeer && parts[1] == _store.PeerToken)
                {
                    _store.PeerHost = peerHost;
                    _store.Save();
                    AdoptStoredPeer();
                    Connected = true;
                    writer.WriteLine("OK");
                    OnChanged?.Invoke();
                }
                else
                {
                    writer.WriteLine("NO");
                }
            }
        }
        catch
        {
            // a dropped pairing connection is not an error
        }
        finally
        {
            try
            {
                c.Close();
            }
            catch
            {
                // already closed
            }
        }
    }

    public void RequestPair(Discovered d, Action<bool, string> done)
    {
        var thread = new Thread(() =>
        {
            try
            {
                var client = new TcpClient();
                IAsyncResult connect = client.BeginConnect(d.Host, d.PairPort, null, null);
                if (!connect.AsyncWaitHandle.WaitOne(4000))
                {
                    done(false, "no answer from " + d.Pretty);
                    return;
                }

                client.EndConnect(connect);
                client.ReceiveTimeout = 60000;

                NetworkStream stream = client.GetStream();
                var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\n", AutoFlush = true };
                var reader = new StreamReader(stream, Encoding.ASCII);

                writer.WriteLine("PAIR " + _myName);
                string? line = reader.ReadLine();
                client.Close();

                string[] parts = (line ?? "").Trim().Split(' ');
                if (parts.Length >= 2 && parts[0] == "OK")
                {
                    _store.PeerName = d.Name;
                    _store.PeerHost = d.Host;
                    _store.PeerToken = parts[1];
                    _store.Save();
                    AdoptStoredPeer();
                    Connected = true;
                    done(true, "paired with " + d.Pretty);
                }
                else
                {
                    done(false, parts.Length > 0 && parts[0] == "NO"
                        ? "declined on the phone"
                        : "no answer from " + d.Pretty);
                }
            }
            catch (Exception ex)
            {
                done(false, "could not reach " + d.Pretty + ": " + ex.Message);
            }

            OnChanged?.Invoke();
        })
        { IsBackground = true };

        thread.Start();
    }

    public void Unpair()
    {
        _store.Forget();
        _token = [];
        _peer = null;
        Connected = false;
        _lastSeq = -1;
        StatusLine = "Open Gyropad on your phone and tap Connect. Both devices must be on the same Wi-Fi.";
        OnChanged?.Invoke();
    }

    private static string NewToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private void DropIfStale()
    {
        if (!Connected || Environment.TickCount - _w.TickAtLastPacket < LinkDeadMs)
            return;

        Connected = false;
        _lastSeq = -1;
        _pendingButtons = 0;
        StatusLine = "The phone stopped responding. Open Gyropad on it to reconnect.";

        try
        {
            Output.FeedWheel(0.0, 0.0, 0.0, 0.0, 0.0, 0);
        }
        catch
        {
            // device may be gone
        }
        try
        {
            Output.FeedPad(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0);
        }
        catch
        {
            // device may be gone
        }
        try
        {
            if (Output.PadPlugged)
            {
                Output.FeedPad(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0);
                Output.UnplugPad();
            }
        }
        catch
        {
            // device may be gone
        }

        _w.Zero();
        _p.Zero();
        DeadButtons = 0;
        PedalCaps = 0;
        PedalLabels = 0;
        PedalLabels2 = 0;
        CamStickWanted = false;
        UsingPad = false;
        WheelViaPad = false;
        PadBecauseGame = false;
        OnChanged?.Invoke();
    }

    private void InputLoop()
    {
        const int preferred = Wire.PortInput;
        string? owner = null;

        try
        {
            _input = new UdpClient(preferred);
        }
        catch (SocketException)
        {
            owner = Linux.PortOwner.Describe(preferred);
            try
            {
                _input = new UdpClient(0);
            }
            catch (Exception ex)
            {
                InputFault = ex is SocketException
                    ? "Gyropad could not open any network port. Something on this PC is blocking it - a firewall or security program is the usual cause."
                    : "Gyropad could not open a network port.";
                StatusLine = InputFault;
                return;
            }
        }
        catch (Exception)
        {
            InputFault = "Gyropad could not open a network port.";
            StatusLine = InputFault;
            return;
        }

        InputPort = ((IPEndPoint)_input.Client.LocalEndPoint!).Port;
        if (_store.Port != InputPort)
        {
            _store.Port = InputPort;
            try
            {
                _store.Save();
            }
            catch
            {
                // settings are best effort
            }
        }

        if (InputPort != preferred)
        {
            InputNote = "Port " + preferred + " is being used by " + (owner ?? "another program")
                + ", so Gyropad moved to port " + InputPort
                + ". Phones on Gyropad 2.0 or newer follow automatically.";
        }

        var any = new IPEndPoint(IPAddress.Any, InputPort);
        int packets = 0;
        int rateFrom = Environment.TickCount;
        try
        {
            _input.Client.ReceiveTimeout = 250;
        }
        catch
        {
            // not supported, receive will block
        }

        while (_run)
        {
            IPEndPoint remote = any;
            byte[] datagram;
            try
            {
                datagram = _input.Receive(ref remote);
            }
            catch (SocketException)
            {
                DropIfStale();
                continue;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            try
            {
                while (_input.Available > 0)
                {
                    IPEndPoint newer = any;
                    byte[] next = _input.Receive(ref newer);
                    if (next.Length > 0)
                    {
                        datagram = next;
                        remote = newer;
                    }
                }
            }
            catch (SocketException)
            {
                // queue drained
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            // An empty datagram would index past the end below. The Windows build
            // walked off the array here; guard instead of losing the input thread.
            if (datagram.Length == 0)
                continue;

            bool isSettings = false;
            bool isHold = false;
            bool isLook = false;
            bool isFace = false;
            bool isPad = false;

            if (datagram.Length == Wire.PktLen && datagram[0] == Wire.MagicIn && datagram[1] == Wire.Version)
            {
                // wheel v2
            }
            else if (datagram.Length == Wire.PktLenLook && datagram[0] == Wire.MagicIn && datagram[1] == Wire.VersionLook)
            {
                isLook = true;
            }
            else if (datagram.Length == Wire.PktPadLen && datagram[0] == Wire.MagicPad && datagram[1] == Wire.VersionPad)
            {
                isPad = true;
            }
            else if (datagram.Length == Wire.PktSettingsLen && datagram[0] == Wire.MagicSettings && datagram[1] == Wire.VersionSettings)
            {
                isSettings = true;
            }
            else if (datagram.Length == Wire.PktHoldLen && datagram[0] == Wire.MagicHold && datagram[1] == Wire.VersionHold)
            {
                isHold = true;
            }
            else
            {
                bool face = datagram[0] == Wire.MagicFace
                    && ((datagram.Length == Wire.PktFaceLen && datagram[1] == Wire.VersionFace)
                        || (datagram.Length == Wire.PktFaceLenV3 && datagram[1] == Wire.VersionFaceV3)
                        || (datagram.Length == Wire.PktFaceLenV2 && datagram[1] == Wire.VersionFaceV2)
                        || (datagram.Length == Wire.PktFaceLenV1 && datagram[1] == Wire.VersionFaceV1));
                if (!face)
                    continue;
                isFace = true;
            }

            if (_peer == null || _token.Length == 0)
                continue;

            int tagOffset;
            if (isFace)
            {
                tagOffset = datagram.Length switch
                {
                    Wire.PktFaceLen => 40,
                    Wire.PktFaceLenV3 => 16,
                    Wire.PktFaceLenV2 => 12,
                    _ => 8
                };
            }
            else if (isHold)
            {
                tagOffset = 8;
            }
            else if (isSettings)
            {
                tagOffset = 16;
            }
            else if (isPad)
            {
                tagOffset = 24;
            }
            else
            {
                tagOffset = isLook ? 24 : 20;
            }

            if (!Wire.VerifyTag(_token, datagram, tagOffset))
            {
                _w.Rejected++;
                continue;
            }

            if (!remote.Address.Equals(_peer) && !IPAddress.IsLoopback(remote.Address))
            {
                if (Environment.TickCount - _lastGoodRx < RoamQuietMs)
                {
                    _w.Rejected++;
                    continue;
                }

                _peer = Unmap(remote.Address);
                _store.PeerHost = _peer.ToString();
                _store.Save();
                AdoptStoredPeer();
                Connected = true;
                OnChanged?.Invoke();
            }

            _lastGoodRx = Environment.TickCount;

            if (isLook)
            {
                _w.LookX = BitConverter.ToInt16(datagram, 20) / 32767f;
                _w.LookY = BitConverter.ToInt16(datagram, 22) / 32767f;
            }
            else if (!isPad && !isSettings && !isHold && !isFace)
            {
                _w.LookX = 0f;
                _w.LookY = 0f;
            }

            if (isFace)
            {
                ApplyFace(datagram);
                continue;
            }

            if (isHold)
            {
                HoldWhat = datagram[2];
                HoldProgress = datagram[3] / 255f;
                HoldAt = Environment.TickCount;
                continue;
            }

            if (isSettings)
            {
                if (_assist != null)
                {
                    _assist.Strength = BitConverter.ToUInt16(datagram, 2) / 1000f;
                    _assist.DeadbandDeg = BitConverter.ToUInt16(datagram, 4) / 10f;
                    float fullAt = BitConverter.ToUInt16(datagram, 6) / 10f;
                    if (fullAt >= 1f)
                        _assist.FullAtDeg = fullAt;
                }

                _store.LockToLockMs = BitConverter.ToUInt16(datagram, 8);
                PhoneLayout.SetAspect(BitConverter.ToUInt16(datagram, 10) / 1000f);
                int lockDegrees = BitConverter.ToUInt16(datagram, 12);
                if (lockDegrees >= 1)
                    _store.LockDegrees = lockDegrees;
                _store.SteerDeadzone = BitConverter.ToUInt16(datagram, 14) / 1000f;
                SettingsAt = Environment.TickCount;
                continue;
            }

            int sequence = BitConverter.ToUInt16(datagram, 2);
            if (sequence == _lastSeq)
                continue;
            _lastSeq = sequence;

            if (isPad)
            {
                _p.LxRaw = BitConverter.ToInt16(datagram, 4);
                _p.LyRaw = BitConverter.ToInt16(datagram, 6);
                _p.RxRaw = BitConverter.ToInt16(datagram, 8);
                _p.RyRaw = BitConverter.ToInt16(datagram, 10);
                _p.LtRaw = BitConverter.ToUInt16(datagram, 12);
                _p.RtRaw = BitConverter.ToUInt16(datagram, 14);
                _p.Buttons = BitConverter.ToUInt16(datagram, 16);
                _p.Hat = datagram[18];
                _echoTMs = BitConverter.ToUInt32(datagram, 20);
            }
            else
            {
                _w.SteerRaw = BitConverter.ToInt16(datagram, 4);
                _w.ThrRaw = BitConverter.ToUInt16(datagram, 6);
                _w.BrkRaw = BitConverter.ToUInt16(datagram, 8);
                _w.CluRaw = BitConverter.ToUInt16(datagram, 10);
                _w.HbRaw = BitConverter.ToUInt16(datagram, 12);
                _w.Buttons = BitConverter.ToUInt16(datagram, 14);
                _echoTMs = BitConverter.ToUInt32(datagram, 16);
            }

            bool backlogged = false;
            try
            {
                backlogged = _input.Available > 0;
            }
            catch
            {
                // socket may be closing
            }

            if (backlogged)
            {
                // Keep short button taps even when several datagrams are queued.
                _pendingButtons |= (ushort)(isPad ? _p.Buttons : _w.Buttons);
                _w.TickAtLastPacket = Environment.TickCount;
                continue;
            }

            if (_pendingButtons != 0)
            {
                if (isPad)
                    _p.Buttons |= _pendingButtons;
                else
                    _w.Buttons |= _pendingButtons;
                _pendingButtons = 0;
            }

            if (isPad != PadMode)
            {
                PadMode = isPad;
                if (isPad)
                {
                    _w.Zero();
                    Output.FeedWheel(0.0, 0.0, 0.0, 0.0, 0.0, 0);
                }
                else
                {
                    _p.Zero();
                    Output.FeedPad(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0);
                }
            }

            _w.TickAtLastPacket = Environment.TickCount;
            Connected = true;
            StatusLine = isPad ? "Connected as a gamepad" : "Connected";
            Route(isPad);
            _telemTo = remote;

            packets++;
            if (Environment.TickCount - rateFrom >= 1000)
            {
                _w.Rate = packets;
                packets = 0;
                rateFrom = Environment.TickCount;
            }
        }

        try
        {
            _input?.Close();
        }
        catch
        {
            // shutting down
        }
    }

    private void ApplyFace(byte[] datagram)
    {
        int oldMode = FaceMode;
        int oldPad = 0;
        int oldSys = 0;
        for (int i = 0; i < 4; i++)
            oldPad = oldPad * 4 + FacePad[i];
        for (int i = 0; i < 3; i++)
            oldSys = oldSys * 4 + FaceSys[i];

        bool oldTop = FaceTopSwapped;
        bool oldCluster = FaceClusterSwapped;

        byte padBits = datagram[3];
        byte sysBits = datagram[4];
        byte flags = datagram[5];

        FaceMode = datagram[2];
        for (int i = 0; i < 4; i++)
            FacePad[i] = (padBits >> (i * 2)) & 3;
        for (int i = 0; i < 3; i++)
            FaceSys[i] = (sysBits >> (i * 2)) & 3;

        FaceTopSwapped = (flags & 1) != 0;
        FaceClusterSwapped = (flags & 2) != 0;
        FaceWheelHeld = (datagram[6] & 1) != 0;

        if (datagram.Length >= 16)
        {
            for (int i = 0; i < 4; i++)
            {
                float gamma = datagram[8 + i] / 50f;
                FaceGamma[i] = gamma > 0.05f && gamma < 6f ? gamma : 1f;
            }
        }
        else
        {
            for (int i = 0; i < 4; i++)
                FaceGamma[i] = 1f;
        }

        if (datagram.Length >= 20)
        {
            float curve = datagram[7] / 200f;
            FaceCurveK = curve > 0.05f && curve <= 1.2f ? curve : 0f;
            FaceDegPerUnit = datagram[12] | (datagram[13] << 8);
            FaceBacklashDeg = datagram[14] / 20f;
        }
        else
        {
            FaceCurveK = 0f;
            FaceDegPerUnit = 0;
            FaceBacklashDeg = 0f;
        }

        if (!IsPerm(FacePad, 4) || !IsPerm(FaceSys, 3))
        {
            for (int i = 0; i < 4; i++)
                FacePad[i] = i;
            for (int i = 0; i < 3; i++)
                FaceSys[i] = i;
        }

        float oldOffSum = 0f;
        for (int i = 0; i < FaceOff.Length; i++)
            oldOffSum += FaceOff[i] * (i + 1);

        bool oldLinked = FaceClusterLinked && FaceStickLinked;

        if (datagram.Length >= 44)
        {
            for (int i = 0; i < 11; i++)
            {
                short value = (short)(datagram[16 + i * 2] | (datagram[17 + i * 2] << 8));
                FaceOff[i] = value / 10000f;
            }

            FaceClusterLinked = (datagram[15] & 1) != 0;
            FaceStickLinked = (datagram[15] & 2) != 0;
            FaceOffSeen = true;
        }

        float newOffSum = 0f;
        for (int i = 0; i < FaceOff.Length; i++)
            newOffSum += FaceOff[i] * (i + 1);

        bool first = !FaceSeen;
        FaceSeen = true;

        int newPad = 0;
        int newSys = 0;
        for (int i = 0; i < 4; i++)
            newPad = newPad * 4 + FacePad[i];
        for (int i = 0; i < 3; i++)
            newSys = newSys * 4 + FaceSys[i];

        bool changed = first
            || oldMode != FaceMode
            || oldPad != newPad
            || oldSys != newSys
            || oldTop != FaceTopSwapped
            || oldCluster != FaceClusterSwapped
            || oldOffSum != newOffSum
            || oldLinked != (FaceClusterLinked && FaceStickLinked);

        if (changed)
            OnFaceChanged?.Invoke();
    }

    private static bool IsPerm(int[] a, int n)
    {
        int seen = 0;
        for (int i = 0; i < n; i++)
        {
            if (a[i] < 0 || a[i] >= n)
                return false;
            int bit = 1 << a[i];
            if ((seen & bit) != 0)
                return false;
            seen |= bit;
        }

        return true;
    }

    private void TelemetryLoop()
    {
        while (_run)
        {
            try
            {
                IPEndPoint? to = _telemTo;
                if (to != null && Connected)
                    SendTelemetry(to);
            }
            catch
            {
                // telemetry must never break input
            }

            Thread.Sleep(50);
        }
    }

    private void SendTelemetry(IPEndPoint to)
    {
        try
        {
            _og.Expire();
            _sim?.Expire();

            var payload = new byte[Wire.TelemLenV4];
            payload[0] = Wire.MagicTelem;
            payload[1] = Wire.VersionTelem;
            payload[2] = (byte)(_og.Live ? (uint)_og.Gear : 255u);

            int flags = 0;
            if (_og.Live)
            {
                if (_og.Redline)
                    flags |= 1;
                if (_og.Traction)
                    flags |= 2;
                if (_og.Abs)
                    flags |= 4;
            }
            payload[3] = (byte)flags;

            PutU16(payload, 4, (ushort)Math.Min(65535, _og.Rpm));
            PutU16(payload, 6, (ushort)Math.Min(65535, _og.SpeedKmhX10));

            int haptic = 0;
            int strength = 0;
            if (_og.Live)
            {
                if (_og.Gear != _lastGear && _lastGear != -99)
                {
                    haptic = Wire.HapGear;
                    strength = 170;
                }
                else if (_og.Abs)
                {
                    haptic = Wire.HapAbs;
                    strength = 190;
                }
                else if (_og.Traction)
                {
                    haptic = Wire.HapTraction;
                    strength = 150;
                }
                else if (_og.Redline)
                {
                    haptic = Wire.HapRedline;
                    strength = 130;
                }
                _lastGear = _og.Gear;
            }

            if (_pendingHaptic != 0)
            {
                haptic = _pendingHaptic;
                strength = 255;
                if (--_pokeRepeats <= 0)
                    _pendingHaptic = 0;
            }

            payload[8] = (byte)haptic;
            payload[9] = (byte)strength;
            PutU32(payload, 10, _echoTMs);

            short slip = 0;
            short yaw = 0;
            int simFlags = 0;
            if (_sim != null && _sim.Live)
            {
                slip = (short)Clamp(_sim.SlipDeg * 100f, -32000f, 32000f);
                yaw = (short)Clamp(_sim.YawRateDeg * 10f, -32000f, 32000f);
                simFlags |= 1;
            }

            PutU16(payload, 14, unchecked((ushort)slip));
            PutU16(payload, 16, unchecked((ushort)yaw));

            if (UsingPad)
                simFlags |= 2;
            if (CamStickWanted)
                simFlags |= 4;
            payload[18] = (byte)simFlags;
            payload[19] = (byte)(UsingPad ? (uint)PedalCaps : 0u);
            payload[26] = (byte)(UsingPad ? (uint)PedalLabels : 0u);
            payload[27] = (byte)(UsingPad ? (uint)PedalLabels2 : 0u);

            string car = _og.Car;
            for (int i = 0; i < 4; i++)
                payload[20 + i] = (byte)(i < car.Length ? car[i] : '\0');

            PutU16(payload, 24, (ushort)(UsingPad ? (uint)DeadButtons : 0u));

            _input?.Send(payload, payload.Length, to);
        }
        catch
        {
            // the phone may have gone away
        }
    }

    private static void PutU16(byte[] b, int at, ushort v)
    {
        b[at] = (byte)v;
        b[at + 1] = (byte)(v >> 8);
    }

    private static void PutU32(byte[] b, int at, uint v)
    {
        b[at] = (byte)v;
        b[at + 1] = (byte)(v >> 8);
        b[at + 2] = (byte)(v >> 16);
        b[at + 3] = (byte)(v >> 24);
    }

    public void PokeHaptic(int id)
    {
        _pendingHaptic = id;
        _pokeRepeats = 4;
    }
}
