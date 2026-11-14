using Gyropad.Core.Config;
using Gyropad.Core.Input;
using Gyropad.Core.Linux;
using Gyropad.Core.State;
using Gyropad.Core.Transport;

namespace Gyropad.Linux;

/// <summary>
/// Builds the <c>runtime.json</c> document that <c>--status</c> reads. Both the
/// window and headless modes publish through this so the two reports agree.
/// </summary>
internal static class StatusSnapshot
{
    public static RuntimeStatus Capture(
        string mode,
        Store store,
        Net net,
        UinputBridge output,
        Wheel wheel,
        Pad pad,
        long startedAtUnixMs)
    {
        return new RuntimeStatus
        {
            Pid = Environment.ProcessId,
            StartedAtUnixMs = startedAtUnixMs,
            Mode = mode,
            Connected = net.Connected,
            PadMode = net.PadMode,
            WheelViaPad = net.WheelViaPad,
            PadBecauseGame = net.PadBecauseGame,
            PeerName = store.PeerName,
            PeerHost = store.PeerHost,
            StatusLine = net.Status,
            InputPort = net.InputPort,
            PairPort = net.PairPort,
            InputFault = net.InputFault,
            DeviceStatus = output.Status,
            DeviceDetail = output.Detail,
            Steer = (float)wheel.Steer,
            Throttle = (float)wheel.Thr,
            Brake = (float)wheel.Brk,
            Clutch = (float)wheel.Clu,
            Handbrake = (float)wheel.Hb,
            Buttons = wheel.Buttons,
            LeftX = (float)pad.Lx,
            LeftY = (float)pad.Ly,
            RightX = (float)pad.Rx,
            RightY = (float)pad.Ry,
            LeftTrigger = (float)pad.Lt,
            RightTrigger = (float)pad.Rt,
            PadButtons = pad.Buttons
        };
    }

    /// <summary>One-line description of the device the game is currently given.</summary>
    public static string Persona(Net net)
    {
        if (!net.Connected)
            return net.InputPort > 0 ? "waiting, port " + net.InputPort : "starting";

        if (net.PadMode)
            return net.PadBecauseGame
                ? "gamepad, because " + net.ActiveProfile.Name + " needs one"
                : "gamepad (Xbox 360)";

        if (net.WheelViaPad)
            return "wheel translated to a gamepad for " + net.ActiveProfile.Name;

        return "wheel / joystick, port " + net.InputPort;
    }
}
