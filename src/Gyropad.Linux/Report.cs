using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Gyropad.Core.Config;
using Gyropad.Core.Linux;
using Gyropad.Core.State;

namespace Gyropad.Linux;

/// <summary>
/// <c>--report</c>: writes one text file with everything needed to diagnose a
/// problem on a machine we cannot log in to. It is read-only apart from the
/// doctor's own settings-directory probe, redacts the pairing token, and can be
/// run while another instance is running.
/// </summary>
internal static class Report
{
    public static int Run(string? output = null)
    {
        string text = Build();
        string path = output ?? Path.Combine(
            Environment.CurrentDirectory,
            "gyropad-report-"
                + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                + ".txt");

        try
        {
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Could not write " + path + ": " + ex.Message);
            return 1;
        }

        Console.WriteLine("Wrote " + path);
        Console.WriteLine("Fill in the three lines at the top, then send the file or paste its contents.");
        return 0;
    }

    public static string Build()
    {
        var text = new StringBuilder();

        text.AppendLine(AppInfo.Name + " " + AppInfo.Version + " - diagnostic report");
        text.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        text.AppendLine("host      " + Environment.MachineName + " (" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant() + ")");
        text.AppendLine();
        text.AppendLine("Fill these in before sending:");
        text.AppendLine("  what I did:      ");
        text.AppendLine("  what I expected: ");
        text.AppendLine("  what happened:   ");
        text.AppendLine();

        RuntimeStatus? status = TryReadStatus();

        text.AppendLine("== Environment (--doctor) ==");
        // Only skip the port checks when a live instance really owns them; a
        // stale runtime.json must not hide that the ports are free (or taken
        // by something else).
        bool live = status != null && RuntimeStatus.IsProcessAlive(status.Pid);
        foreach (string line in Doctor.Collect(skipPorts: live).Lines)
            text.AppendLine(line);
        text.AppendLine();

        text.AppendLine("== Running instance ==");
        AppendStatus(text, status);
        text.AppendLine();

        text.AppendLine("== Settings (pairing token redacted) ==");
        AppendSettings(text);
        text.AppendLine();

        text.AppendLine("== Gyropad input devices (/sys/class/input) ==");
        AppendDevices(text);
        text.AppendLine();

        text.AppendLine("== Network interfaces ==");
        AppendInterfaces(text);
        text.AppendLine();

        text.AppendLine("== If the axes or buttons are wrong ==");
        text.AppendLine("  evtest            (pick the Gyropad device, move one control at a time)");
        text.AppendLine("  jstest /dev/input/jsN");
        text.AppendLine("  Include that output too: it shows which axis or button each control drives.");

        return text.ToString();
    }

    private static RuntimeStatus? TryReadStatus()
    {
        try
        {
            return RuntimeStatus.Read();
        }
        catch
        {
            return null;
        }
    }

    private static void AppendStatus(StringBuilder text, RuntimeStatus? status)
    {
        if (status == null)
        {
            text.AppendLine("not running (" + RuntimeStatus.StatusPath + " not found)");
            return;
        }

        bool alive = RuntimeStatus.IsProcessAlive(status.Pid);
        if (!alive)
        {
            text.AppendLine("stale     yes - pid " + status.Pid
                + " is gone; the lines below describe a session that already ended");
        }

        text.AppendLine("version   " + (status.Version.Length > 0 ? status.Version : "unknown")
            + (string.Equals(status.Version, AppInfo.Version, StringComparison.Ordinal)
                ? ""
                : "  (this report was made by " + AppInfo.Version + ")"));
        text.AppendLine("mode      " + status.Mode);
        text.AppendLine("pid       " + status.Pid
            + (alive ? " (alive)" : " (not running)"));
        text.AppendLine("uptime    " + status.Uptime.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
        text.AppendLine("link      " + (status.Connected
            ? status.PadMode ? "connected, gamepad" : "connected, wheel"
            : "waiting for the phone"));
        text.AppendLine("phone     " + (status.PeerName.Length > 0
            ? Pretty(status.PeerName) + " at " + status.PeerHost
            : "not paired"));
        text.AppendLine("status    " + status.StatusLine);
        text.AppendLine("ports     input UDP " + status.InputPort + "  pairing TCP " + status.PairPort);
        text.AppendLine("device    " + status.DeviceStatus
            + (status.DeviceDetail.Length > 0 ? " - " + status.DeviceDetail : ""));
        text.AppendLine("route     " + status.RouteDescription);
        if (status.InputFault.Length > 0)
            text.AppendLine("fault     " + status.InputFault);

        if (status.Connected)
        {
            text.AppendLine("packets   wheel " + Rate(status.WheelRate, status.WheelAgeMs)
                + "   gamepad " + Rate(status.PadRate, status.PadAgeMs));
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "steer     {0}  throttle {1:0.00}  brake {2:0.00}  clutch {3:0.00}  handbrake {4:0.00}  buttons 0x{5:x4}",
                Fit(status.Steer), status.Throttle, status.Brake, status.Clutch, status.Handbrake, status.Buttons));
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "pad       lx {0}  ly {1}  rx {2}  ry {3}  lt {4:0.00}  rt {5:0.00}  buttons 0x{6:x4}",
                Fit(status.LeftX), Fit(status.LeftY), Fit(status.RightX), Fit(status.RightY),
                status.LeftTrigger, status.RightTrigger, status.PadButtons));
        }
    }

    private static void AppendSettings(StringBuilder text)
    {
        text.AppendLine("path      " + Store.SettingsPath);
        if (!File.Exists(Store.SettingsPath))
        {
            text.AppendLine("(not created yet; defaults apply)");
            return;
        }

        try
        {
            foreach (string line in File.ReadLines(Store.SettingsPath))
                text.AppendLine(Redact.SettingsLine(line));
        }
        catch (Exception ex)
        {
            text.AppendLine("(could not read: " + ex.Message + ")");
        }
    }

    private static void AppendDevices(StringBuilder text)
    {
        string root = "/sys/class/input";
        if (!Directory.Exists(root))
        {
            text.AppendLine("(no " + root + "; this is not Linux)");
            return;
        }

        bool any = false;
        try
        {
            foreach (string dir in Directory.GetDirectories(root, "event*").OrderBy(d => d, StringComparer.Ordinal))
            {
                string device = Path.Combine(dir, "device");
                string name = ReadTrim(Path.Combine(device, "name"));
                // The pad persona is named "Microsoft X-Box 360 pad" on purpose,
                // so match against the bridge's own names rather than the app name.
                if (name.Length == 0 || !UinputBridge.IsVirtualDeviceName(name))
                    continue;

                any = true;
                text.AppendLine(Path.GetFileName(dir) + "  \"" + name + "\"");
                text.AppendLine("  id       bus " + ReadTrim(Path.Combine(device, "id/bus"))
                    + "  vendor " + ReadTrim(Path.Combine(device, "id/vendor"))
                    + "  product " + ReadTrim(Path.Combine(device, "id/product")));
                text.AppendLine("  keys     " + ReadTrim(Path.Combine(device, "capabilities/key")));
                text.AppendLine("  abs      " + ReadTrim(Path.Combine(device, "capabilities/abs")));
            }
        }
        catch (Exception ex)
        {
            text.AppendLine("(could not read " + root + ": " + ex.Message + ")");
            return;
        }

        if (!any)
            text.AppendLine("(no input device with \"" + AppInfo.Name + "\" in its name)");
    }

    private static void AppendInterfaces(StringBuilder text)
    {
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                         .OrderBy(n => n.Name, StringComparer.Ordinal))
            {
                IPInterfaceProperties props = ni.GetIPProperties();
                string v4 = string.Join(", ", props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString()));
                string v6 = string.Join(", ", props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal)
                    .Select(a => a.Address.ToString()));

                text.AppendLine(ni.Name + "  " + ni.NetworkInterfaceType + "  " + ni.OperationalStatus
                    + (v4.Length > 0 ? "  ipv4 " + v4 : "")
                    + (v6.Length > 0 ? "  ipv6 " + v6 : ""));
            }
        }
        catch (Exception ex)
        {
            text.AppendLine("(could not list interfaces: " + ex.Message + ")");
        }
    }

    private static string ReadTrim(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    private static string Pretty(string name) => name.Replace('_', ' ');

    private static string Rate(int rate, int ageMs)
    {
        if (ageMs < 0)
            return "none yet";

        int live = ageMs > 1500 ? 0 : rate;
        string ago = ageMs >= 1000
            ? (ageMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s"
            : ageMs.ToString(CultureInfo.InvariantCulture) + " ms";

        return live.ToString(CultureInfo.InvariantCulture) + "/s, last " + ago + " ago";
    }

    private static string Fit(float value)
        => value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
}
