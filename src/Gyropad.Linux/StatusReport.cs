using System;
using System.Globalization;
using Gyropad.Core.State;

namespace Gyropad.Linux;

/// <summary><c>--status</c>: prints the snapshot the running instance publishes.</summary>
internal static class StatusReport
{
    public static int Run()
    {
        RuntimeStatus? status = RuntimeStatus.Read();
        if (status == null)
        {
            Console.Error.WriteLine("No Gyropad is running (no " + RuntimeStatus.StatusPath + ").");
            Console.Error.WriteLine("Start one with: Gyropad --headless");
            return 1;
        }

        bool alive = RuntimeStatus.IsProcessAlive(status.Pid);

        Console.WriteLine("Gyropad " + AppInfo.Version + " - " + status.Mode
            + " - pid " + status.Pid + " - up " + FormatUptime(status.Uptime));

        string link = status.Connected
            ? status.PadMode ? "connected, gamepad" : "connected, wheel"
            : "waiting for the phone";
        Console.WriteLine("link       " + link);

        Console.WriteLine("phone      "
            + (status.PeerName.Length > 0
                ? Pretty(status.PeerName) + " at " + status.PeerHost
                : "not paired"));

        if (status.StatusLine.Length > 0)
            Console.WriteLine("status     " + status.StatusLine);

        if (status.InputPort > 0)
            Console.WriteLine("input      UDP " + status.InputPort);
        if (status.PairPort > 0)
            Console.WriteLine("pairing    TCP " + status.PairPort);

        Console.WriteLine("device     " + status.DeviceStatus
            + (status.DeviceDetail.Length > 0 ? " - " + status.DeviceDetail : ""));

        if (status.InputFault.Length > 0)
            Console.WriteLine("fault      " + status.InputFault);

        if (status.Connected)
        {
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "steer      {0}  throttle {1:0.00}  brake {2:0.00}  clutch {3:0.00}  handbrake {4:0.00}  buttons 0x{5:x4}",
                Fit(status.Steer), status.Throttle, status.Brake, status.Clutch, status.Handbrake, status.Buttons));

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "pad        lx {0}  ly {1}  rx {2}  ry {3}  lt {4:0.00}  rt {5:0.00}  buttons 0x{6:x4}",
                Fit(status.LeftX), Fit(status.LeftY), Fit(status.RightX), Fit(status.RightY),
                status.LeftTrigger, status.RightTrigger, status.PadButtons));
        }

        if (!alive)
        {
            Console.WriteLine();
            Console.WriteLine("pid " + status.Pid + " is not running; this file was left by an earlier session.");
            return 1;
        }

        return 0;
    }

    private static string Pretty(string name) => name.Replace('_', ' ');

    private static string Fit(float value)
        => value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

    private static string FormatUptime(TimeSpan uptime)
        => uptime.TotalDays >= 1
            ? uptime.ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture)
            : uptime.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}
