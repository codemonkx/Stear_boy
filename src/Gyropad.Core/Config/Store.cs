using System.Globalization;

namespace Gyropad.Core.Config;

/// <summary>
/// Persistent settings. The file format is the original flat INI, so an existing
/// Windows settings file can be copied to the Linux config directory unchanged.
/// Location: <c>$GYROPAD_CONFIG_DIR</c>, else <c>$XDG_CONFIG_HOME/gyropad</c>,
/// else <c>~/.config/gyropad</c>.
/// </summary>
public sealed class Store
{
    public const int WheelTrialMs = 60000;

    public string PeerName = "";
    public string PeerHost = "";
    public string PeerToken = "";
    public string VJoyDll = "";
    public string GameExe = "";

    public int Port = 5005;
    public int OutSimPort = 30001;

    public float AssistStrength;
    public float AssistDeadbandDeg = 6f;
    public float AssistFullAtDeg = 35f;
    public bool AssistInvert;

    public volatile int LockDegrees = 180;
    public volatile float SteerDeadzone = 0.02f;
    public volatile int LockToLockMs;

    public bool ForceWheel;
    public bool FirewallAsked;
    public string ForceWheelExe = "";

    public volatile string WheelTrialExe = "";
    public volatile int WheelTrialUntil;

    public bool DriversAsked;
    public int UpdateCheckedAt;
    public string UpdateSkipped = "";

    public readonly List<Binding> Bindings = [];

    public static string ConfigDirectory
    {
        get
        {
            string? overrideDir = Environment.GetEnvironmentVariable("GYROPAD_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDir))
                return overrideDir;

            string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdg))
                return Path.Combine(xdg, "gyropad");

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
                home = Environment.GetEnvironmentVariable("HOME") ?? ".";
            return Path.Combine(home, ".config", "gyropad");
        }
    }

    public static string SettingsPath => Path.Combine(ConfigDirectory, "settings.ini");

    public bool HasPeer => PeerToken.Length > 0 && PeerHost.Length > 0;

    public void Load()
    {
        try
        {
            string path = SettingsPath;
            if (!File.Exists(path))
                return;

            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();

                switch (key)
                {
                    case "peerName":
                        PeerName = value;
                        break;
                    case "peerHost":
                        PeerHost = value;
                        break;
                    case "peerToken":
                        PeerToken = value;
                        break;
                    case "vjoyDll":
                        VJoyDll = value;
                        break;
                    case "gameExe":
                        GameExe = value;
                        break;
                    case "port":
                        if (int.TryParse(value, out int port) && port > 0 && port < 65536)
                            Port = port;
                        break;
                    case "outsimPort":
                        if (int.TryParse(value, out int osPort) && osPort > 0 && osPort < 65536)
                            OutSimPort = osPort;
                        break;
                    case "assist":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float assist))
                            AssistStrength = assist;
                        break;
                    case "assistDeadband":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float deadband))
                            AssistDeadbandDeg = deadband;
                        break;
                    case "assistFullAt":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float fullAt) && fullAt >= 1f)
                            AssistFullAtDeg = fullAt;
                        break;
                    case "assistInvert":
                        AssistInvert = value == "1";
                        break;
                    case "forceWheel":
                        ForceWheel = value == "1";
                        break;
                    case "firewallAsked":
                        FirewallAsked = value == "1";
                        break;
                    case "forceWheelExe":
                        ForceWheelExe = value;
                        break;
                    case "driversAsked":
                        DriversAsked = value == "1";
                        break;
                    case "updateSkipped":
                        UpdateSkipped = value;
                        break;
                    case "bind":
                    {
                        string[] parts = value.Split('|');
                        if (parts.Length == 3 && parts[0].Length > 0)
                        {
                            if (!int.TryParse(parts[2], out int device) || device < 0 || device > 2)
                                device = Binding.DevProfile;
                            Bindings.Add(new Binding
                            {
                                Exe = parts[0],
                                ProfileExe = parts[1],
                                Device = device
                            });
                        }
                        break;
                    }
                }
            }
        }
        catch
        {
            // A corrupt settings file must never prevent startup; defaults apply.
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            var lines = new List<string>
            {
                "peerName=" + PeerName,
                "peerHost=" + PeerHost,
                "peerToken=" + PeerToken,
                "vjoyDll=" + VJoyDll,
                "gameExe=" + GameExe,
                "port=" + Port.ToString(CultureInfo.InvariantCulture),
                "outsimPort=" + OutSimPort.ToString(CultureInfo.InvariantCulture),
                "assist=" + AssistStrength.ToString("0.00", CultureInfo.InvariantCulture),
                "assistDeadband=" + AssistDeadbandDeg.ToString("0.0", CultureInfo.InvariantCulture),
                "assistFullAt=" + AssistFullAtDeg.ToString("0.0", CultureInfo.InvariantCulture),
                "assistInvert=" + (AssistInvert ? "1" : "0"),
                "forceWheel=" + (ForceWheel ? "1" : "0"),
                "firewallAsked=" + (FirewallAsked ? "1" : "0"),
                "forceWheelExe=" + ForceWheelExe,
                "driversAsked=" + (DriversAsked ? "1" : "0"),
                "updateSkipped=" + UpdateSkipped
            };

            foreach (Binding binding in Bindings)
            {
                lines.Add("bind=" + binding.Exe + "|" + binding.ProfileExe + "|"
                          + binding.Device.ToString(CultureInfo.InvariantCulture));
            }

            File.WriteAllLines(SettingsPath, lines);
        }
        catch
        {
            // Saving is best effort; the app keeps running with in-memory state.
        }
    }

    public void Forget()
    {
        PeerName = "";
        PeerHost = "";
        PeerToken = "";
        Save();
    }
}
