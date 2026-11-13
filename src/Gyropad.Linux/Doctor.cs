using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Gyropad.Core.Config;
using Gyropad.Core.Linux;
using Gyropad.Core.Protocol;

namespace Gyropad.Linux;

/// <summary>
/// Environment checks behind <c>--doctor</c>. Everything here is read-only
/// except for a short write test inside the settings directory and a transient
/// socket bind, so it is safe to run before or after the app.
/// </summary>
internal static class Doctor
{
    public static int Run()
    {
        (bool ok, IReadOnlyList<string> lines) = Collect();
        foreach (string line in lines)
            Console.WriteLine(line);
        return ok ? 0 : 1;
    }

    public static (bool Ok, IReadOnlyList<string> Lines) Collect()
    {
        var lines = new List<string>();
        bool fatal = false;

        void Head(string text) => lines.Add(text);

        void Pass(string label, string? detail = null)
            => lines.Add("  [ ok ] " + label + (string.IsNullOrEmpty(detail) ? "" : "  - " + detail));

        void Warn(string label, string? detail = null)
            => lines.Add("  [warn] " + label + (string.IsNullOrEmpty(detail) ? "" : "  - " + detail));

        void Fail(string label, string? detail = null)
        {
            fatal = true;
            lines.Add("  [fail] " + label + (string.IsNullOrEmpty(detail) ? "" : "  - " + detail));
        }

        Head(AppInfo.Name + " " + AppInfo.Version + " doctor");
        Head("");

        Head("System");
        Pass("runtime", RuntimeInformation.FrameworkDescription);
        Pass("os", RuntimeInformation.OSDescription.Trim());
        Pass("architecture", RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Pass("platform", "Linux");
        else
            Fail("platform", "this build talks to the Linux uinput API");

        Head("");
        Head("Virtual input (uinput)");
        string node = "/dev/uinput";
        if (!File.Exists(node))
        {
            Fail("device node", node + " is missing");
            Head("         load the module once with: sudo modprobe uinput");
            Head("         make it permanent with: packaging/install-udev-rule.sh");
        }
        else
        {
            Pass("device node", node);
            (bool writable, string why) = CanOpenForWrite(node);
            if (writable)
                Pass("access", "read/write");
            else
                Fail("access", why);
        }

        bool moduleListed = false;
        try
        {
            if (File.Exists("/proc/modules"))
                moduleListed = File.ReadLines("/proc/modules").Any(l => l.StartsWith("uinput ", StringComparison.Ordinal));
        }
        catch
        {
            // /proc is not readable in some sandboxes
        }

        if (moduleListed)
            Pass("kernel module", "uinput is loaded");
        else if (File.Exists(node))
            Pass("kernel module", "not listed (built into the kernel or already loaded)");
        else
            Warn("kernel module", "uinput is not loaded");

        if (Directory.Exists("/dev/input"))
            Pass("event devices", "/dev/input is present");
        else
            Warn("event devices", "/dev/input is missing; is this a container?");

        Head("");
        Head("Network");
        (bool udpIn, string udpInDetail) = UdpFree(Wire.PortInput);
        if (udpIn)
            Pass("input port " + Wire.PortInput, "free");
        else
            Warn("input port " + Wire.PortInput, udpInDetail + " (the app will pick another port)");

        (bool udpDiscovery, string udpDiscoveryDetail) = UdpFree(Wire.PortDiscovery);
        if (udpDiscovery)
            Pass("discovery port " + Wire.PortDiscovery, "free");
        else
            Warn("discovery port " + Wire.PortDiscovery, udpDiscoveryDetail + " (phones may not appear)");

        (bool tcpPair, string tcpPairDetail) = TcpFree(Wire.PortPair);
        if (tcpPair)
            Pass("pairing port " + Wire.PortPair, "free");
        else
            Warn("pairing port " + Wire.PortPair, tcpPairDetail + " (the app falls back to 5008-5011)");

        foreach (int port in new[] { Wire.PortOutGauge, Wire.PortOutSim })
        {
            (bool free, string detail) = UdpFree(port);
            if (free)
                Pass("telemetry port " + port, "free");
            else
                Warn("telemetry port " + port, detail);
        }

        Head("");
        Head("Settings");
        Head("  directory  " + Store.ConfigDirectory);
        (bool configWritable, string configDetail) = CanWriteDirectory(Store.ConfigDirectory);
        if (configWritable)
            Pass("writable", Store.SettingsPath);
        else
            Fail("writable", configDetail);

        if (File.Exists(Store.SettingsPath))
            Pass("settings file", Store.SettingsPath);
        else
            Warn("settings file", "not created yet; the defaults apply until the first save");

        Head("");
        Head("Desktop session");
        string session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "";
        string display = Environment.GetEnvironmentVariable("DISPLAY") ?? "";
        string wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "";
        if (session.Length > 0)
            Pass("session type", session);
        if (display.Length > 0)
            Pass("X11 display", display);
        if (wayland.Length > 0)
            Pass("Wayland display", wayland);
        if (display.Length == 0 && wayland.Length == 0)
            Warn("display", "neither DISPLAY nor WAYLAND_DISPLAY is set; the window needs a desktop session");

        Head("");
        Head(fatal ? "Result: problems found. See the [fail] lines above." : "Result: ready to run.");
        return (!fatal, lines);
    }

    private static (bool Ok, string Detail) CanOpenForWrite(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            return (true, "");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "permission denied. Install the udev rule (packaging/install-udev-rule.sh) or run once as root");
        }
        catch (IOException ex)
        {
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static (bool Ok, string Detail) CanWriteDirectory(string path)
    {
        string probe = Path.Combine(path, ".doctor-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(path);
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static (bool Ok, string Detail) UdpFree(int port)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            return (true, "free");
        }
        catch (SocketException ex)
        {
            string? owner = PortOwner.Describe(port);
            return (false, owner == null ? ex.Message : "used by " + owner);
        }
    }

    private static (bool Ok, string Detail) TcpFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
            return (true, "free");
        }
        catch (SocketException ex)
        {
            string? owner = PortOwner.Describe(port);
            return (false, owner == null ? ex.Message : "used by " + owner);
        }
    }
}
