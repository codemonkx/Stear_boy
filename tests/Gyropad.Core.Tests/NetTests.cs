using System.Net;
using System.Net.Sockets;
using System.Text;
using Gyropad.Core.Config;
using Gyropad.Core.Input;
using Gyropad.Core.Protocol;
using Gyropad.Core.Telemetry;
using Gyropad.Core.Titles;
using Gyropad.Core.Transport;

namespace Gyropad.Core.Tests;

/// <summary>
/// End-to-end tests for the phone link: real loopback sockets, a fake output
/// device. Each test gets a fresh paired link and a private config directory.
/// </summary>
public class NetTests
{
    private sealed record WheelFrame(double Steer, double Thr, double Brk, double Clu, double Hb, int Buttons);

    private sealed record PadFrame(double Lx, double Ly, double Rx, double Ry, double Lt, double Rt, int Buttons);

    private sealed class FakeOutput : IOutputDevice
    {
        private readonly object _gate = new();
        private readonly List<WheelFrame> _wheels = [];
        private readonly List<PadFrame> _pads = [];

        public bool WheelReady { get; set; } = true;

        public bool PadPlugged { get; private set; }

        public int PlugCount { get; private set; }

        public WheelFrame[] Wheels
        {
            get
            {
                lock (_gate)
                    return [.. _wheels];
            }
        }

        public PadFrame[] Pads
        {
            get
            {
                lock (_gate)
                    return [.. _pads];
            }
        }

        public WheelFrame? LastWheel => Wheels.LastOrDefault();

        public PadFrame? LastPad => Pads.LastOrDefault();

        public void PlugPad()
        {
            lock (_gate)
            {
                PadPlugged = true;
                PlugCount++;
            }
        }

        public void UnplugPad()
        {
            lock (_gate)
                PadPlugged = false;
        }

        public void FeedWheel(double steer, double throttle, double brake, double clutch, double handbrake, int buttons)
        {
            lock (_gate)
                _wheels.Add(new WheelFrame(steer, throttle, brake, clutch, handbrake, buttons));
        }

        public void FeedPad(double lx, double ly, double rx, double ry, double lt, double rt, int buttons)
        {
            lock (_gate)
                _pads.Add(new PadFrame(lx, ly, rx, ry, lt, rt, buttons));
        }
    }

    private sealed class NoProcesses : Games.IProcessSource
    {
        public IReadOnlyList<Games.ProcInfo> List() => [];
    }

    private sealed class FixedProcesses(params Games.ProcInfo[] items) : Games.IProcessSource
    {
        public IReadOnlyList<Games.ProcInfo> List() => items;
    }

    /// <summary>One started <see cref="Net"/> plus the fake phone that talks to it.</summary>
    private sealed class Harness : IDisposable
    {
        private readonly string _dir;
        private readonly string? _previousConfigDir;
        private readonly Store _store = new();
        private readonly UdpClient _phone = new(new IPEndPoint(IPAddress.Loopback, 0));
        private ushort _seq = 100;

        public readonly Wheel Wheel = new();
        public readonly Pad Pad = new();
        public readonly OutGauge Gauge = new();
        public readonly FakeOutput Output = new();
        public readonly DriftAssist Assist = new();
        public readonly Net Net;

        public Harness()
        {
            _dir = Path.Combine(Path.GetTempPath(), "gyropad-net-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _previousConfigDir = Environment.GetEnvironmentVariable("GYROPAD_CONFIG_DIR");
            Environment.SetEnvironmentVariable("GYROPAD_CONFIG_DIR", _dir);

            Games.ProcessSource = new NoProcesses();
            Games.Bound = null;
            Games.Detect(force: true);

            Net = new Net(_store, Wheel, Pad, Output, Gauge, sim: null, Assist, "TestPC")
            {
                OnApprovalRequest = _ => true
            };
            Net.Start();
        }

        public Store Store => _store;

        public string Token => _store.PeerToken;

        private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
        {
            int started = Environment.TickCount;
            while (Environment.TickCount - started < timeoutMs)
            {
                if (condition())
                    return true;
                Thread.Sleep(10);
            }

            return condition();
        }

        private int InputPort
        {
            get
            {
                WaitUntil(() => Net.InputPort > 0);
                return Net.InputPort;
            }
        }

        public string Pair(string name = "Pixel_7")
        {
            WaitUntil(() => Net.PairPort > 0);
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, Net.PairPort);
            client.ReceiveTimeout = 5000;

            using NetworkStream stream = client.GetStream();
            using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\n", AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.ASCII);
            writer.WriteLine("PAIR " + name);
            return reader.ReadLine() ?? "";
        }

        public string Say(string line)
        {
            WaitUntil(() => Net.PairPort > 0);
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, Net.PairPort);
            client.ReceiveTimeout = 5000;

            using NetworkStream stream = client.GetStream();
            using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\n", AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.ASCII);
            writer.WriteLine(line);
            return reader.ReadLine() ?? "";
        }

        public void PairAndWait(string name = "Pixel_7")
        {
            string reply = Pair(name);
            Assert.StartsWith("OK ", reply);
        }

        private void SendPacket(byte[] packet, int tagOffset, bool validTag = true)
        {
            byte[] token = Encoding.ASCII.GetBytes(_store.PeerToken);
            uint tag = validTag ? Wire.Tag(token, packet, tagOffset) : 0xDEADBEEF;
            BitConverter.GetBytes(tag).CopyTo(packet, tagOffset);
            _phone.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, InputPort));
        }

        public void SendWheel(short steer = 0, ushort thr = 0, ushort brk = 0, ushort clu = 0,
            ushort hb = 0, ushort buttons = 0, uint echo = 0, bool validTag = true)
        {
            var packet = new byte[Wire.PktLen];
            packet[0] = Wire.MagicIn;
            packet[1] = Wire.Version;
            PutU16(packet, 2, ++_seq);
            PutU16(packet, 4, unchecked((ushort)steer));
            PutU16(packet, 6, thr);
            PutU16(packet, 8, brk);
            PutU16(packet, 10, clu);
            PutU16(packet, 12, hb);
            PutU16(packet, 14, buttons);
            PutU32(packet, 16, echo);
            SendPacket(packet, 20, validTag);
        }

        public void SendLook(short x, short y)
        {
            var packet = new byte[Wire.PktLenLook];
            packet[0] = Wire.MagicIn;
            packet[1] = Wire.VersionLook;
            PutU16(packet, 2, ++_seq);
            PutU16(packet, 20, unchecked((ushort)x));
            PutU16(packet, 22, unchecked((ushort)y));
            SendPacket(packet, 24);
        }

        public void SendPad(short lx, short ly, short rx, short ry, ushort lt, ushort rt, ushort buttons, byte hat)
        {
            var packet = new byte[Wire.PktPadLen];
            packet[0] = Wire.MagicPad;
            packet[1] = Wire.VersionPad;
            PutU16(packet, 2, ++_seq);
            PutU16(packet, 4, unchecked((ushort)lx));
            PutU16(packet, 6, unchecked((ushort)ly));
            PutU16(packet, 8, unchecked((ushort)rx));
            PutU16(packet, 10, unchecked((ushort)ry));
            PutU16(packet, 12, lt);
            PutU16(packet, 14, rt);
            PutU16(packet, 16, buttons);
            packet[18] = hat;
            SendPacket(packet, 24);
        }

        public void SendSettings(ushort strengthPerMille, ushort deadbandTenths, ushort fullAtTenths,
            ushort lockToLockMs, ushort aspectPerMille, ushort lockDegrees, ushort deadzonePerMille)
        {
            var packet = new byte[Wire.PktSettingsLen];
            packet[0] = Wire.MagicSettings;
            packet[1] = Wire.VersionSettings;
            PutU16(packet, 2, strengthPerMille);
            PutU16(packet, 4, deadbandTenths);
            PutU16(packet, 6, fullAtTenths);
            PutU16(packet, 8, lockToLockMs);
            PutU16(packet, 10, aspectPerMille);
            PutU16(packet, 12, lockDegrees);
            PutU16(packet, 14, deadzonePerMille);
            SendPacket(packet, 16);
        }

        public void SendHold(byte what, byte progress)
        {
            var packet = new byte[Wire.PktHoldLen];
            packet[0] = Wire.MagicHold;
            packet[1] = Wire.VersionHold;
            packet[2] = what;
            packet[3] = progress;
            SendPacket(packet, 8);
        }

        public void SendFace(byte mode, byte padBits, byte sysBits, byte flags, byte wheelHeld,
            byte curve = 0, byte gamma = 50, ushort degPerUnit = 0, byte backlash = 0, byte linked = 0,
            short firstOffset = 0)
        {
            var packet = new byte[Wire.PktFaceLen];
            packet[0] = Wire.MagicFace;
            packet[1] = Wire.VersionFace;
            packet[2] = mode;
            packet[3] = padBits;
            packet[4] = sysBits;
            packet[5] = flags;
            packet[6] = wheelHeld;
            packet[7] = curve;
            for (int i = 0; i < 4; i++)
                packet[8 + i] = gamma;
            PutU16(packet, 12, degPerUnit);
            packet[14] = backlash;
            packet[15] = linked;
            PutU16(packet, 16, unchecked((ushort)firstOffset));
            SendPacket(packet, 40);
        }

        public void Announce(string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            _phone.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, Wire.PortDiscovery));
        }

        public byte[] ReceiveTelemetry(int timeoutMs = 3000)
        {
            _phone.Client.ReceiveTimeout = timeoutMs;
            _phone.Client.ReceiveBufferSize = 4096;
            var from = new IPEndPoint(IPAddress.Any, 0);
            return _phone.Receive(ref from);
        }

        public static bool WaitFor(Func<bool> condition, int timeoutMs = 5000) => WaitUntil(condition, timeoutMs);

        public void Dispose()
        {
            Net.Stop();
            _phone.Dispose();
            Environment.SetEnvironmentVariable("GYROPAD_CONFIG_DIR", _previousConfigDir);
            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch
            {
                // best effort
            }
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

    private static void UseGame(string exe)
    {
        Games.ProcessSource = new FixedProcesses(new Games.ProcInfo { Pid = 7, Name = exe, Title = exe, Rss = 1024 });
        Games.Bound = null;
        Games.Detect(force: true);
    }

    [Fact]
    public void PairingStoresThePeerAndIssuesAToken()
    {
        using var h = new Harness();

        string reply = h.Pair("Pixel_7");

        Assert.StartsWith("OK ", reply);
        string token = reply[3..];
        Assert.Equal(32, token.Length);
        Assert.Matches("^[0-9a-f]{32}$", token);
        Assert.Equal(token, h.Token);
        Assert.Equal("Pixel_7", h.Store.PeerName);
        Assert.Equal("127.0.0.1", h.Store.PeerHost);
        Assert.True(h.Net.Connected);
        Assert.True(File.Exists(Store.SettingsPath));
        Assert.Contains("peerToken=" + token, File.ReadAllLines(Store.SettingsPath));
    }

    [Fact]
    public void HelloRejectsAWrongTokenAndAcceptsTheStoredOne()
    {
        using var h = new Harness();
        h.PairAndWait();

        Assert.Equal("NO", h.Say("HELLO 00000000000000000000000000000000"));
        Assert.Equal("OK", h.Say("HELLO " + h.Token));
        Assert.True(h.Net.Connected);
    }

    [Fact]
    public void WheelPacketDrivesTheWheelDevice()
    {
        using var h = new Harness();
        h.PairAndWait();

        h.SendWheel(steer: 16384, thr: 32767, brk: 65535, clu: 6553, hb: 128, buttons: 0b10);

        Assert.True(Harness.WaitFor(() => h.Output.LastWheel != null));
        WheelFrame frame = h.Output.LastWheel!;
        Assert.Equal(0.5, frame.Steer, 3);
        Assert.Equal(0.5, frame.Thr, 3);
        Assert.Equal(1.0, frame.Brk, 3);
        Assert.Equal(0.1, frame.Clu, 3);
        Assert.Equal(128 / 65535.0, frame.Hb, 6);
        Assert.Equal(0b10, frame.Buttons);
        Assert.True(h.Net.Connected);
        Assert.Equal("Connected", h.Net.StatusLine);
        Assert.False(h.Output.PadPlugged);
        Assert.False(h.Net.UsingPad);
    }

    [Fact]
    public void LookPacketUpdatesTheCameraAxesAndAWheelPacketClearsThem()
    {
        using var h = new Harness();
        h.PairAndWait();

        h.SendLook(16384, -8192);
        Assert.True(Harness.WaitFor(() => h.Wheel.LookX != 0f));
        Assert.Equal(0.5f, h.Wheel.LookX, 3);
        Assert.Equal(-0.25f, h.Wheel.LookY, 3);

        h.SendWheel(steer: 1000);
        Assert.True(Harness.WaitFor(() => h.Wheel.LookX == 0f && h.Wheel.LookY == 0f));
    }

    [Fact]
    public void PacketWithABadTagIsRejected()
    {
        using var h = new Harness();
        h.PairAndWait();

        h.SendWheel(steer: 16384, validTag: false);

        Assert.True(Harness.WaitFor(() => h.Wheel.Rejected >= 1));
        Assert.Empty(h.Output.Wheels);
    }

    [Fact]
    public void PadPacketSwitchesToTheGamepadPersona()
    {
        using var h = new Harness();
        h.PairAndWait();

        h.SendWheel(steer: 16384);
        Assert.True(Harness.WaitFor(() => h.Output.LastWheel is { Steer: > 0.4 }));

        h.SendPad(lx: 16384, ly: -16384, rx: 0, ry: 8192, lt: 65535, rt: 32768,
            buttons: 1, hat: 2);

        Assert.True(Harness.WaitFor(() => h.Output.LastPad != null));
        PadFrame frame = h.Output.LastPad!;
        Assert.Equal(0.5, frame.Lx, 3);
        Assert.Equal(0.5, frame.Ly, 3);   // the pad path inverts Y
        Assert.Equal(0.0, frame.Rx, 3);
        Assert.Equal(-0.25, frame.Ry, 3);
        Assert.Equal(1.0, frame.Lt, 3);
        Assert.Equal(0.5, frame.Rt, 3);
        Assert.Equal(0x1000 | 8, frame.Buttons); // cross + d-pad down

        Assert.True(h.Net.PadMode);
        Assert.True(h.Net.UsingPad);
        Assert.True(h.Output.PadPlugged);
        Assert.False(h.Net.WheelViaPad);
        Assert.Equal("Connected as a gamepad", h.Net.StatusLine);

        // Switching persona zeroes the wheel so a held steering axis cannot stick.
        WheelFrame lastWheel = h.Output.LastWheel!;
        Assert.Equal(0.0, lastWheel.Steer);
    }

    [Fact]
    public void WheelPacketIsTranslatedForTitlesThatNeedAPad()
    {
        using var h = new Harness();
        h.PairAndWait();
        UseGame("forzamotorsport7");

        h.SendWheel(steer: 16384, thr: 32767, brk: 6553);

        Assert.True(Harness.WaitFor(() => h.Output.LastPad != null));
        PadFrame frame = h.Output.LastPad!;
        Assert.Equal(0.5, frame.Lx, 3);
        Assert.Equal(0.0, frame.Ly, 3);
        Assert.Equal(0.1, frame.Lt, 3);   // brake on the left trigger
        Assert.Equal(0.5, frame.Rt, 3);   // throttle on the right trigger

        Assert.True(h.Output.PadPlugged);
        Assert.True(h.Net.WheelViaPad);
        Assert.True(h.Net.PadBecauseGame);
        Assert.True(h.Net.UsingPad);
        Assert.Equal("Forza Motorsport 7", h.Net.ActiveProfile.Name);
    }

    [Fact]
    public void SettingsPacketUpdatesTheAssistDeadzoneAndLayout()
    {
        using var h = new Harness();
        h.PairAndWait();

        h.SendSettings(strengthPerMille: 750, deadbandTenths: 45, fullAtTenths: 280,
            lockToLockMs: 900, aspectPerMille: 2160, lockDegrees: 360, deadzonePerMille: 35);

        Assert.True(Harness.WaitFor(() => h.Store.LockDegrees == 360));
        Assert.Equal(0.75f, h.Assist.Strength, 3);
        Assert.Equal(4.5f, h.Assist.DeadbandDeg, 3);
        Assert.Equal(28f, h.Assist.FullAtDeg, 3);
        Assert.Equal(900, h.Store.LockToLockMs);
        Assert.Equal(360, h.Store.LockDegrees);
        Assert.Equal(0.035f, h.Store.SteerDeadzone, 3);
        Assert.Equal(1080f * 2.16f, PhoneLayout.W, 1);
        Assert.True(h.Net.SettingsAt != 0);
    }

    [Fact]
    public void HoldPacketReportsItsProgress()
    {
        using var h = new Harness();
        h.PairAndWait();

        h.SendHold(Wire.HoldMode, 128);

        Assert.True(Harness.WaitFor(() => h.Net.HoldWhat == Wire.HoldMode));
        Assert.Equal(128 / 255f, h.Net.HoldProgress, 3);
        Assert.True(h.Net.HoldLive);
    }

    [Fact]
    public void TelemetryDescribesTheCarAndEchoesTheTimestamp()
    {
        using var h = new Harness();
        h.PairAndWait();

        h.Gauge.Live = true;
        h.Gauge.LastAt = Environment.TickCount;
        h.Gauge.Gear = 3;
        h.Gauge.Rpm = 4200;
        h.Gauge.SpeedKmhX10 = 1234;
        h.Gauge.ShowLights = 1 | 0x400; // redline + ABS
        h.Gauge.Car = "LFS";

        h.SendWheel(steer: 0, echo: 0x11223344);
        byte[] telem = h.ReceiveTelemetry();

        Assert.Equal(Wire.TelemLenV4, telem.Length);
        Assert.Equal(Wire.MagicTelem, telem[0]);
        Assert.Equal(Wire.VersionTelem, telem[1]);
        Assert.Equal(3, telem[2]);
        Assert.Equal(1 | 4, telem[3]);
        Assert.Equal(4200, BitConverter.ToUInt16(telem, 4));
        Assert.Equal(1234, BitConverter.ToUInt16(telem, 6));
        Assert.Equal(0x11223344u, BitConverter.ToUInt32(telem, 10));
        Assert.Equal((byte)'L', telem[20]);
        Assert.Equal((byte)'F', telem[21]);
        Assert.Equal((byte)'S', telem[22]);
        Assert.Equal(0, telem[23]);
        Assert.Equal(0, telem[18]); // no OutSim, not a pad, no camera stick
    }

    [Fact]
    public void FacePacketUpdatesTheButtonMap()
    {
        using var h = new Harness();
        h.PairAndWait();

        int changes = 0;
        h.Net.OnFaceChanged = () => Interlocked.Increment(ref changes);

        h.SendFace(mode: 2, padBits: 0xE1, sysBits: 0x24, flags: Wire.FaceTopSwapped,
            wheelHeld: 1, gamma: 100, degPerUnit: 900, backlash: 20, linked: 3, firstOffset: 2500);

        Assert.True(Harness.WaitFor(() => changes > 0));
        Assert.Equal(2, h.Net.FaceMode);
        Assert.Equal([1, 0, 2, 3], h.Net.FacePad);
        Assert.Equal([0, 1, 2], h.Net.FaceSys); // 0x24 is not a permutation, so it resets
        Assert.True(h.Net.FaceTopSwapped);
        Assert.False(h.Net.FaceClusterSwapped);
        Assert.True(h.Net.FaceWheelHeld);
        Assert.Equal(2f, h.Net.FaceGamma[0], 3);
        Assert.Equal(900, h.Net.FaceDegPerUnit);
        Assert.Equal(1f, h.Net.FaceBacklashDeg, 3);
        Assert.Equal(0.25f, h.Net.FaceOff[0], 4);
        Assert.True(h.Net.FaceOffSeen);
        Assert.True(h.Net.FaceClusterLinked);
        Assert.True(h.Net.FaceStickLinked);
    }

    [Fact]
    public void UnpairForgetsThePeerSoPacketsAreIgnored()
    {
        using var h = new Harness();
        h.PairAndWait();

        h.Net.Unpair();

        Assert.False(h.Store.HasPeer);
        Assert.False(h.Net.Connected);

        h.SendWheel(steer: 16384);
        Thread.Sleep(300);

        Assert.Empty(h.Output.Wheels);
    }

    [Fact]
    public void DiscoveryRemembersAnnouncedPhones()
    {
        using var h = new Harness();

        h.Announce("PW2 ANNOUNCE PHONE Pixel_7 7507");

        Assert.True(Harness.WaitFor(() => h.Net.Snapshot().Any(d => d.Name == "Pixel_7")));
        Discovered phone = h.Net.Snapshot().Single(d => d.Name == "Pixel_7");
        Assert.Equal("127.0.0.1", phone.Host);
        Assert.Equal(Wire.PortPairPhone, phone.PairPort);
    }

    [Fact]
    public void PairMeRaisesThePairingRequest()
    {
        using var h = new Harness();
        var wanted = new List<Discovered>();
        h.Net.OnPairWanted = d => wanted.Add(d);

        // The documented four-token form.
        h.Announce("PW2 PAIRME Pixel_8 7507");
        Assert.True(Harness.WaitFor(() => wanted.Count == 1));
        Assert.Equal("Pixel_8", wanted[0].Name);
        Assert.Equal("127.0.0.1", wanted[0].Host);
        Assert.Equal(Wire.PortPairPhone, wanted[0].PairPort);

        // The extra-token form the Windows build's parser implied.
        h.Announce("PW2 PAIRME extra Pixel_9 7607");
        Assert.True(Harness.WaitFor(() => wanted.Count == 2));
        Assert.Equal("Pixel_9", wanted[1].Name);
        Assert.Equal(7607, wanted[1].PairPort);

        // An absent port falls back to the phone default.
        h.Announce("PW2 PAIRME Pixel_10");
        Assert.True(Harness.WaitFor(() => wanted.Count == 3));
        Assert.Equal("Pixel_10", wanted[2].Name);
        Assert.Equal(Wire.PortPairPhone, wanted[2].PairPort);
    }

    [Fact]
    public void SilenceDropsTheLinkAndZeroesTheOutputs()
    {
        using var h = new Harness();
        h.PairAndWait();

        h.SendPad(lx: 16384, ly: 0, rx: 0, ry: 0, lt: 0, rt: 0, buttons: 1, hat: 8);
        Assert.True(Harness.WaitFor(() => h.Output.PadPlugged));
        Assert.True(h.Net.Connected);

        // LinkDeadMs is 2000; the input loop notices on its 250 ms timeout.
        // Wait for the whole teardown, not just the flag: DropIfStale flips
        // Connected first and only then zeroes the devices.
        Assert.True(Harness.WaitFor(
            () => !h.Net.Connected && !h.Output.PadPlugged && !h.Net.UsingPad, 8000));
        Assert.False(h.Output.PadPlugged);
        Assert.False(h.Net.UsingPad);
        Assert.Contains("stopped responding", h.Net.StatusLine);
        Assert.Equal(0, h.Wheel.SteerRaw);
    }
}
