using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Gyropad.Core.State;

/// <summary>
/// Snapshot of a running Gyropad instance, written to
/// <c>runtime.json</c> in the config directory so that <c>--status</c> can read
/// the live link without attaching to the process.
/// </summary>
public sealed class RuntimeStatus
{
    public static string StatusPath => Path.Combine(Config.Store.ConfigDirectory, "runtime.json");

    public int Pid { get; set; }

    /// <summary>Gyropad version of the process that wrote this snapshot.</summary>
    public string Version { get; set; } = "";

    public long StartedAtUnixMs { get; set; }

    /// <summary>"headless" or "window".</summary>
    public string Mode { get; set; } = "headless";

    public bool Connected { get; set; }

    public bool PadMode { get; set; }

    public bool WheelViaPad { get; set; }

    public bool PadBecauseGame { get; set; }

    public string PeerName { get; set; } = "";

    public string PeerHost { get; set; } = "";

    public string StatusLine { get; set; } = "";

    public int InputPort { get; set; }

    public int PairPort { get; set; }

    public string InputFault { get; set; } = "";

    public string DeviceStatus { get; set; } = "";

    public string DeviceDetail { get; set; } = "";

    public float Steer { get; set; }

    public float Throttle { get; set; }

    public float Brake { get; set; }

    public float Clutch { get; set; }

    public float Handbrake { get; set; }

    public int Buttons { get; set; }

    public float LeftX { get; set; }

    public float LeftY { get; set; }

    public float RightX { get; set; }

    public float RightY { get; set; }

    public float LeftTrigger { get; set; }

    public float RightTrigger { get; set; }

    public int PadButtons { get; set; }

    public TimeSpan Uptime
    {
        get
        {
            long started = StartedAtUnixMs;
            if (started <= 0)
                return TimeSpan.Zero;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long ms = now - started;
            return ms > 0 ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;
        }
    }

    /// <summary>Best effort: a status file must never break the app or the CLI.</summary>
    public void Write()
    {
        try
        {
            string path = StatusPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, SerializerOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            // status reporting is optional
        }
    }

    public static RuntimeStatus? Read()
    {
        try
        {
            string path = StatusPath;
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<RuntimeStatus>(File.ReadAllText(path), SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            File.Delete(StatusPath);
        }
        catch
        {
            // nothing to do
        }
    }

    /// <summary>
    /// True when the pid still exists. Linux can reuse pids, so this is only
    /// used to distinguish "running" from "the file was left behind".
    /// </summary>
    public static bool IsProcessAlive(int pid)
    {
        if (pid <= 0)
            return false;

        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };
}
