using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Gyropad.Core.Config;
using Gyropad.Core.Input;
using Gyropad.Core.Linux;
using Gyropad.Core.State;
using Gyropad.Core.Telemetry;
using Gyropad.Core.Titles;
using Gyropad.Core.Transport;

namespace Gyropad.Linux;

/// <summary>
/// <c>--headless</c>: the same phone link and virtual devices as the window,
/// driven from a terminal instead of Avalonia. Pairing approval comes from
/// <c>--pair</c>, live state is written to <c>runtime.json</c> for
/// <c>--status</c>, and SIGINT/SIGTERM shut the session down cleanly.
/// </summary>
internal sealed class Headless
{
    private readonly Options _options;
    private readonly Store _store = new();
    private readonly Wheel _wheel = new();
    private readonly Pad _pad = new();
    private readonly OutGauge _gauge = new();
    private readonly OutSim _sim;
    private readonly DriftAssist _assist = new();
    private readonly UinputBridge _output = new();
    private readonly Net _net;

    private readonly long _startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly object _promptGate = new();
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lastPairWanted = new(StringComparer.OrdinalIgnoreCase);

    private int _pairing;
    private bool _warnedRedirected;

    private string _lastStatus = "";
    private bool _lastConnected;
    private string _lastDevice = "";
    private int _lastInputPort;
    private int _lastPairPort;
    private int _lastVerboseAt;

    private Headless(Options options)
    {
        _options = options;

        _store.Load();
        _assist.Strength = _store.AssistStrength;
        _assist.DeadbandDeg = _store.AssistDeadbandDeg;
        _assist.FullAtDeg = _store.AssistFullAtDeg;
        _sim = new OutSim { Port = _store.OutSimPort, Invert = _store.AssistInvert };

        _net = new Net(_store, _wheel, _pad, _output, _gauge, _sim, _assist, Environment.MachineName)
        {
            OnApprovalRequest = ApproveInbound,
            OnPairWanted = OnPairWanted
        };
    }

    public static int Run(Options options) => new Headless(options).RunCore();

    private int RunCore()
    {
        Say("Gyropad " + AppInfo.Version + " - headless");
        Say("settings: " + Store.SettingsPath);
        Say("pairing: " + _options.Pair.ToString().ToLowerInvariant()
            + (_options.Pair == PairPolicy.Ask ? " (approve on this terminal)" : ""));

        _output.Start();
        _gauge.Start();
        _sim.Start();
        Games.Bound = _store;
        Games.StartWatching();
        _net.Start();

        _lastStatus = _net.StatusLine;
        _lastConnected = _net.Connected;
        _lastDevice = DeviceKey();
        _lastInputPort = _net.InputPort;
        _lastPairPort = _net.PairPort;

        Say("Started. " + _net.StatusLine);
        Say("Virtual device: " + _output.Status
            + (_output.Detail.Length > 0 ? " - " + _output.Detail : ""));
        Publish();

        ConsoleCancelEventHandler cancel = (_, e) =>
        {
            e.Cancel = true;
            _stop.Set();
        };
        Console.CancelKeyPress += cancel;

        PosixSignalRegistration? terminate = null;
        PosixSignalRegistration? hangup = null;
        if (!OperatingSystem.IsWindows())
        {
            terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);
            hangup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, OnSignal);
        }

        try
        {
            while (!_stop.Wait(500))
                Tick();
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            terminate?.Dispose();
            hangup?.Dispose();
            Shutdown();
        }

        return 0;
    }

    private void OnSignal(PosixSignalContext context)
    {
        context.Cancel = true;
        _stop.Set();
    }

    private void Tick()
    {
        Publish();

        string status = _net.StatusLine;
        if (status != _lastStatus)
        {
            _lastStatus = status;
            Say(status);
        }

        if (_net.Connected != _lastConnected)
        {
            _lastConnected = _net.Connected;
            Say(_net.Connected ? "Phone linked." : "Phone link closed.");
        }

        string device = DeviceKey();
        if (device != _lastDevice)
        {
            _lastDevice = device;
            Say("Virtual device: " + _output.Status
                + (_output.Detail.Length > 0 ? " - " + _output.Detail : ""));
        }

        if (_net.InputPort != _lastInputPort)
        {
            _lastInputPort = _net.InputPort;
            if (_lastInputPort > 0)
                Say("Listening for phone input on UDP " + _lastInputPort + ".");
        }

        if (_net.PairPort != _lastPairPort)
        {
            _lastPairPort = _net.PairPort;
            if (_lastPairPort > 0)
                Say("Accepting pairing on TCP " + _lastPairPort + ".");
        }

        foreach (Discovered d in _net.Snapshot())
        {
            if (d.Name.Length == 0 || Environment.TickCount - d.SeenAt >= 9000)
                continue;

            string key = d.Name + "@" + d.Host + ":" + d.PairPort;
            if (_seen.Add(key))
                Say("Phone seen: " + d.Pretty + " at " + d.Host + ":" + d.PairPort + ".");
        }

        if (_options.Verbose && _net.Connected && Environment.TickCount - _lastVerboseAt >= 1000)
        {
            _lastVerboseAt = Environment.TickCount;
            Say(DescribeInput());
        }
    }

    private void Publish()
        => StatusSnapshot
            .Capture("headless", _store, _net, _output, _wheel, _pad, _startedAt)
            .Write();

    private void Shutdown()
    {
        Say("Stopping...");
        _net.Stop();
        _gauge.Stop();
        _sim.Stop();
        _output.Dispose();
        RuntimeStatus.Clear();
        Say("Stopped.");
    }

    private string DeviceKey() => _output.Status + "\u0000" + _output.Detail;

    private static string Pretty(string name) => name.Replace('_', ' ');

    private bool ApproveInbound(string rawName)
    {
        string name = Pretty(rawName);

        switch (_options.Pair)
        {
            case PairPolicy.Auto:
                Say(name + " wants to pair; approved (--pair auto).");
                return true;

            case PairPolicy.Deny:
                Say("Declined a pairing request from " + name + " (--pair deny).");
                return false;

            default:
                return Prompt(
                    name + " wants to pair.",
                    "Only continue if you just tapped Connect in Gyropad on this phone.");
        }
    }

    private void OnPairWanted(Discovered d)
    {
        if (_net.Connected || _options.Pair == PairPolicy.Deny)
            return;

        // PAIRME repeats while the phone waits; do not start a second callback
        // for the same address within a few seconds.
        int now = Environment.TickCount;
        lock (_lastPairWanted)
        {
            if (_lastPairWanted.TryGetValue(d.Host, out int at) && unchecked(now - at) < 15000)
                return;
            _lastPairWanted[d.Host] = now;
        }

        var worker = new Thread(() =>
        {
            bool yes = _options.Pair == PairPolicy.Auto
                || Prompt(
                    d.Pretty + " asked this PC to connect.",
                    "The phone could not reach this PC directly. Connect back to it instead?");

            if (!yes)
            {
                Say("Declined the callback request from " + d.Pretty + ".");
                return;
            }

            CallBack(d);
        })
        {
            IsBackground = true,
            Name = "gyropad-pairwanted"
        };
        worker.Start();
    }

    private void CallBack(Discovered d)
    {
        if (Interlocked.Exchange(ref _pairing, 1) == 1)
            return;

        Say("Calling " + d.Pretty + "...");
        try
        {
            _net.RequestPair(d, (_, message) =>
            {
                Say(message);
                Interlocked.Exchange(ref _pairing, 0);
            });
        }
        catch (Exception ex)
        {
            Say("Could not call " + d.Pretty + ": " + ex.Message);
            Interlocked.Exchange(ref _pairing, 0);
        }
    }

    /// <summary>
    /// Terminal approval for inbound pairing. With redirected stdin (systemd,
    /// a pipe, cron) there is nobody to answer, so pairing is declined and the
    /// user is told once how to accept automatically.
    /// </summary>
    private bool Prompt(string title, string note)
    {
        if (Console.IsInputRedirected)
        {
            lock (_promptGate)
            {
                if (!_warnedRedirected)
                {
                    _warnedRedirected = true;
                    Say("No terminal to ask on, so pairing was declined. Use --pair auto to accept without asking.");
                }
            }

            return false;
        }

        lock (_promptGate)
        {
            Console.WriteLine();
            Say(title);
            Console.WriteLine("       " + note);
            Console.Write("       approve? [y/N] ");

            string? line = Console.ReadLine();
            bool yes = line != null
                && (line.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)
                    || line.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase));

            Say(yes ? "Approved." : "Declined.");
            return yes;
        }
    }

    private string DescribeInput()
    {
        string route = "route  " + StatusSnapshot.Persona(_net)
            + "   (wheel " + _net.WheelRate.ToString(CultureInfo.InvariantCulture) + "/s, gamepad "
            + _net.PadRate.ToString(CultureInfo.InvariantCulture) + "/s)";

        string wheel = string.Format(
            CultureInfo.InvariantCulture,
            "input  steer {0}  throttle {1:0.00}  brake {2:0.00}  clutch {3:0.00}  handbrake {4:0.00}  buttons 0x{5:x4}",
            Fit(_wheel.Steer), _wheel.Thr, _wheel.Brk, _wheel.Clu, _wheel.Hb, _wheel.Buttons);

        string pad = string.Format(
            CultureInfo.InvariantCulture,
            "pad    lx {0}  ly {1}  rx {2}  ry {3}  lt {4:0.00}  rt {5:0.00}  buttons 0x{6:x4}",
            Fit(_pad.Lx), Fit(_pad.Ly), Fit(_pad.Rx), Fit(_pad.Ry),
            _pad.Lt, _pad.Rt, _pad.Buttons);

        return route + Environment.NewLine + wheel + Environment.NewLine + "       " + pad;
    }

    private static string Fit(double value) => value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

    private static void Say(string message)
        => Console.WriteLine(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message);
}
