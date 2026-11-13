using Gyropad.Core.Config;

namespace Gyropad.Core.Titles;

/// <summary>
/// Supported-title detection. The title table and selection rules come from the
/// Windows build unchanged; only process enumeration is Linux-specific.
/// </summary>
public static class Games
{
    /// <summary>One running process, as seen by the platform.</summary>
    public sealed class ProcInfo
    {
        public int Pid;
        public string Name = "";
        public string Title = "";
        public long Rss;
    }

    /// <summary>Injectable so detection can be unit-tested off Linux.</summary>
    public interface IProcessSource
    {
        IReadOnlyList<ProcInfo> List();
    }

    /// <summary>Reads process names and RSS from <c>/proc</c>.</summary>
    public sealed class ProcProcessSource : IProcessSource
    {
        public IReadOnlyList<ProcInfo> List()
        {
            var result = new List<ProcInfo>();
            foreach (string dir in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(dir), out int pid))
                    continue;

                string name = ReadName(dir);
                if (name.Length == 0)
                    continue;

                long rss = 0;
                try
                {
                    string[] statm = File.ReadAllText(Path.Combine(dir, "statm")).Split(' ');
                    if (statm.Length > 1 && long.TryParse(statm[1], out long pages))
                        rss = pages * Environment.SystemPageSize;
                }
                catch
                {
                    // process exited while scanning
                }

                result.Add(new ProcInfo { Pid = pid, Name = name, Title = name, Rss = rss });
            }

            return result;
        }

        private static string ReadName(string dir)
        {
            // /proc/<pid>/comm is truncated to 15 characters, which is shorter than
            // several known titles (e.g. "ac2-win64-shipping"), so prefer argv[0].
            try
            {
                string cmdline = File.ReadAllText(Path.Combine(dir, "cmdline"));
                int nul = cmdline.IndexOf('\0');
                string first = nul >= 0 ? cmdline[..nul] : cmdline;
                if (first.Length > 0)
                {
                    string name = Path.GetFileName(first.TrimEnd('/'));
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        name = name[..^4];
                    if (name.Length > 0)
                        return name;
                }
            }
            catch
            {
                // fall through to comm
            }

            try
            {
                return File.ReadAllText(Path.Combine(dir, "comm")).Trim();
            }
            catch
            {
                return "";
            }
        }
    }

    public sealed class Candidate
    {
        public string Exe = "";
        public string Title = "";
        public long Mem;
    }
	private static readonly GameProfile CarParking2Pad = new GameProfile
	{
		Exe = "car parking 2",
		Name = "Car Parking Multiplayer 2",
		NeedsPad = true,
		DigitalThrottle = false,
		DigitalBrake = false,
		Clutch = 512,
		DigitalClutch = true,
		Handbrake = 8192,
		DigitalHandbrake = true,
		FaceB = 256,
		DpadDrivesStick = false,
		CamIsStick = true,
		Horn = 64,
		Ignition = 16384,
		HasLights = false,
		HasMap = false,
		Camera = 0,
		ShiftWithStick = true,
		ShiftHold = 512
	};

	public static readonly GameProfile[] Known = new[]
	{
		new GameProfile
		{
			Exe = "lfs",
			Name = "Live for Speed",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "acs",
			Name = "Assetto Corsa",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "ac2-win64-shipping",
			Name = "Assetto Corsa Competizione",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "beamng.drive",
			Name = "BeamNG.drive",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "rfactor2",
			Name = "rFactor 2",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "ams2",
			Name = "Automobilista 2",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "raceroom",
			Name = "RaceRoom",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "car parking 2",
			Prefix = "car parking",
			Name = "Car Parking Multiplayer 2",
			NeedsPad = false,
			WheelNeedsMapping = true,
			PadFallback = CarParking2Pad
		},
		new GameProfile
		{
			Exe = "rrre",
			Name = "RaceRoom",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "dirtrally2",
			Name = "Dirt Rally 2.0",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "wreckfest",
			Name = "Wreckfest",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "eurotrucks2",
			Name = "Euro Truck Simulator 2",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "amtrucks",
			Name = "American Truck Simulator",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "iracingsim64dx11",
			Name = "iRacing",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "forzahorizon5",
			Name = "Forza Horizon 5",
			NeedsPad = true,
			Horn = 128,
			Lights = 2,
			Handbrake = 4096,
			Map = 32
		},
		new GameProfile
		{
			Exe = "forzahorizon4",
			Name = "Forza Horizon 4",
			NeedsPad = true,
			Horn = 128,
			Lights = 2,
			Handbrake = 4096,
			Map = 32
		},
		new GameProfile
		{
			Exe = "forzahorizon6",
			Name = "Forza Horizon 6",
			NeedsPad = true,
			WheelUntested = true,
			Horn = 128,
			Lights = 2,
			Handbrake = 4096,
			Map = 32
		},
		new GameProfile
		{
			Exe = "forzahorizon",
			Prefix = "forzahorizon",
			Name = "Forza Horizon",
			NeedsPad = true,
			WheelUntested = true,
			Horn = 128,
			Lights = 2,
			Handbrake = 4096,
			Map = 32
		},
		new GameProfile
		{
			Exe = "forza_gaming.desktop.x64_release",
			Prefix = "forza_gaming",
			Name = "Forza Motorsport",
			NeedsPad = true,
			WheelUntested = true
		},
		new GameProfile
		{
			Exe = "thecrew2",
			Name = "The Crew 2",
			NeedsPad = true
		},
		new GameProfile
		{
			Exe = "rocketleague",
			Name = "Rocket League",
			NeedsPad = true
		},
		new GameProfile
		{
			Exe = "trackmania",
			Name = "Trackmania",
			NeedsPad = true,
			HasClutch = false,
			HasHandbrake = false,
			HasGears = false,
			DigitalBrake = true,
			BrakeButton = 256,
			Camera = 512
		},
		new GameProfile
		{
			Exe = "asphalt9_gdk_x64_rtl",
			Prefix = "asphalt",
			Name = "Asphalt Legends Unite",
			NeedsPad = true,
			HasGears = false,
			Clutch = 16384,
			ClutchLabel = 1,
			Handbrake = 4096,
			HandbrakeLabel = 2,
			SteerAntiDeadzone = 0.28,
			Camera = 2,
			HasLights = false,
			ShareFaceButtons = true,
			DigitalThrottle = false,
			ThrottleAntiDeadzone = 0.5,
			BrakeLabel = 1,
			DigitalBrake = true,
			ThrottleButton = 0,
			BrakeButton = 0
		},
		new GameProfile
		{
			Exe = "asphalt8",
			Name = "Asphalt 8: Airborne",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasLights = false,
			HasMap = false,
			HasHorn = false,
			HasIgnition = false,
			Camera = 32768,
			Handbrake = 8192,
			HandbrakeLabel = 1,
			DigitalHandbrake = true,
			HasClutch = true,
			Clutch = 512,
			ClutchLabel = 2,
			DigitalClutch = true,
			SteerAntiDeadzone = 0.28,
			ThrottleAntiDeadzone = 0.5,
			DigitalThrottle = false,
			DigitalBrake = true,
			ThrottleButton = 0,
			BrakeButton = 0,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "grip2-win64-shipping",
			Prefix = "grip2",
			Name = "GRIP 2",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			BrakeAxisFromHandbrake = true,
			DigitalHandbrake = false,
			Handbrake = 0,
			Clutch = 256,
			DigitalClutch = true,
			ClutchLabel = 5,
			DigitalBrake = true,
			BrakeButton = 512,
			BrakeLabel = 6,
			DigitalThrottle = false,
			HasLights = false,
			HasMap = false,
			HasHorn = false,
			HasIgnition = false,
			ShareFaceButtons = true,
			SteerAntiDeadzone = 0.18
		},
		new GameProfile
		{
			Exe = "gta5",
			Name = "Grand Theft Auto V",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "gta5_enhanced",
			Name = "Grand Theft Auto V",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "gtaiv",
			Name = "Grand Theft Auto IV",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "rdr2",
			Name = "Red Dead Redemption 2",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "cyberpunk2077",
			Name = "Cyberpunk 2077",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "watch_dogs",
			Name = "Watch Dogs",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "watchdogs2",
			Name = "Watch Dogs 2",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "watchdogslegion",
			Name = "Watch Dogs: Legion",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "saintsrowiv",
			Name = "Saints Row IV",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "saintsrow",
			Name = "Saints Row",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "mafia3",
			Name = "Mafia III",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "mafiadefinitiveedition",
			Name = "Mafia: Definitive Edition",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "justcause3",
			Name = "Just Cause 3",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "justcause4",
			Name = "Just Cause 4",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "farcry5",
			Name = "Far Cry 5",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "farcry6",
			Name = "Far Cry 6",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "sdhdshipping",
			Name = "Sleeping Dogs",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "daysgone",
			Name = "Days Gone",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "grw",
			Name = "Ghost Recon Wildlands",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "snowrunner",
			Name = "SnowRunner",
			NeedsPad = true,
			WheelUntested = true,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "mudrunner",
			Name = "MudRunner",
			NeedsPad = true,
			WheelUntested = true,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "f1_22",
			Name = "F1 22",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "f1_23",
			Name = "F1 23",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "f1_24",
			Name = "F1 24",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "drt",
			Name = "Dirt Rally",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "dirt4",
			Name = "Dirt 4",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "dirt5",
			Name = "DIRT 5",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "wrc",
			Name = "EA Sports WRC",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "richardburnsrally_sse",
			Name = "Richard Burns Rally",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "pcars2avx",
			Name = "Project CARS 2",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "pcars2",
			Name = "Project CARS 2",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "projectcars3",
			Name = "Project CARS 3",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "ams",
			Name = "Automobilista",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "rfactor",
			Name = "rFactor",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "race07",
			Name = "RACE 07",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "lemansultimate",
			Name = "Le Mans Ultimate",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "carx",
			Name = "CarX Drift Racing Online",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "drift legends 2",
			Name = "Drift Legends 2",
			NeedsPad = true,
			HasGears = false,
			HasClutch = false,
			Camera = 256,
			Handbrake = 8192,
			Lights = 16384,
			ShareFaceButtons = true,
			WheelUntested = true
		},
		new GameProfile
		{
			Exe = "farmingsimulator2022",
			Name = "Farming Simulator 22",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "farmingsimulator2025",
			Name = "Farming Simulator 25",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "nfs11",
			Name = "NFS Hot Pursuit",
			NeedsPad = true,
			HasGears = false,
			ShareFaceButtons = true,
			Handbrake = 16384,
			HandbrakeLabel = 0,
			Clutch = 4096,
			ClutchLabel = 2,
			Lights = 8192,
			Camera = 512
		},
		new GameProfile
		{
			Exe = "nfs11remastered",
			Name = "NFS Hot Pursuit Remastered",
			NeedsPad = true,
			HasGears = false,
			ShareFaceButtons = true,
			Handbrake = 16384,
			HandbrakeLabel = 0,
			Clutch = 4096,
			ClutchLabel = 2,
			Lights = 8192,
			Camera = 512
		},
		new GameProfile
		{
			Exe = "needforspeedheat",
			Name = "Need for Speed Heat",
			NeedsPad = true,
			Handbrake = 16384,
			HandbrakeLabel = 0,
			Clutch = 4096,
			ClutchLabel = 2,
			Camera = 32768,
			Horn = 64,
			Map = 32,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "needforspeedunbound",
			Name = "Need for Speed Unbound",
			NeedsPad = true,
			Handbrake = 512,
			HandbrakeLabel = 0,
			Clutch = 16384,
			ClutchLabel = 2,
			GearUp = 256,
			GearDown = 4096,
			Horn = 8192,
			Map = 32,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "needforspeedpayback",
			Name = "Need for Speed Payback",
			NeedsPad = true,
			WheelUntested = true,
			Handbrake = 16384,
			HandbrakeLabel = 0,
			Clutch = 4096,
			ClutchLabel = 2,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "nfs13",
			Name = "Need for Speed Most Wanted",
			NeedsPad = true,
			WheelUntested = true,
			Handbrake = 16384,
			HandbrakeLabel = 0,
			Clutch = 4096,
			ClutchLabel = 2,
			HasGears = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "nfs14",
			Name = "Need for Speed Rivals",
			NeedsPad = true,
			WheelUntested = true,
			Handbrake = 16384,
			HandbrakeLabel = 0,
			Clutch = 4096,
			ClutchLabel = 2,
			HasGears = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "nfs14_x86",
			Name = "Need for Speed Rivals",
			NeedsPad = true,
			WheelUntested = true,
			Handbrake = 16384,
			HandbrakeLabel = 0,
			Clutch = 4096,
			ClutchLabel = 2,
			HasGears = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "needforspeed",
			Name = "Need for Speed (2015)",
			NeedsPad = true,
			WheelUntested = true,
			Handbrake = 16384,
			HandbrakeLabel = 0,
			Clutch = 4096,
			ClutchLabel = 2,
			HasGears = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "need for speed the run",
			Name = "Need for Speed The Run",
			NeedsPad = true,
			WheelUntested = true,
			Handbrake = 16384,
			HandbrakeLabel = 0,
			Clutch = 4096,
			ClutchLabel = 2,
			HasGears = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "shift2u",
			Name = "Shift 2 Unleashed",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "shift2",
			Name = "Shift 2 Unleashed",
			NeedsPad = false
		},
		new GameProfile
		{
			Exe = "forzahorizon3",
			Name = "Forza Horizon 3",
			NeedsPad = true,
			WheelUntested = true,
			Horn = 128,
			Lights = 2,
			Handbrake = 4096,
			Map = 32
		},
		new GameProfile
		{
			Exe = "forzamotorsport7",
			Name = "Forza Motorsport 7",
			NeedsPad = true,
			WheelUntested = true
		},
		new GameProfile
		{
			Exe = "thecrew",
			Name = "The Crew",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "thecrewmotorfest",
			Name = "The Crew Motorfest",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "burnoutpr",
			Name = "Burnout Paradise Remastered",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "gridlegends",
			Name = "GRID Legends",
			NeedsPad = true,
			WheelUntested = true,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "grid",
			Name = "GRID",
			NeedsPad = true,
			WheelUntested = true,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "hotwheelsunleashed",
			Name = "Hot Wheels Unleashed",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		},
		new GameProfile
		{
			Exe = "artofrally",
			Name = "art of rally",
			NeedsPad = true,
			WheelUntested = true,
			HasGears = false,
			HasClutch = false,
			ShareFaceButtons = true
		}
	};

	public static Store? Bound;

	public static readonly GameProfile Generic = new GameProfile
	{
		Exe = "",
		Name = "",
		NeedsPad = false
	};

    private static volatile int _checkedAt;

    private static volatile GameProfile _current = Generic;

    private static volatile string _runningName = "";

    public static volatile int LastScanAt;

    private static Thread? _watcher;

    private static Candidate[] _cands = [];

    public static volatile string UnknownExe = "";

    public static volatile string MatchedExe = "";

    public static IProcessSource ProcessSource { get; set; } = new ProcProcessSource();

    private static readonly string[] NotGames =
    [
        "explorer", "searchhost", "shellexperiencehost", "startmenuexperiencehost",
        "textinputhost", "applicationframehost", "systemsettings", "taskmgr", "gyropadpc",
        "steam", "steamwebhelper", "epicgameslauncher", "battle.net", "origin", "eadesktop",
        "ubisoftconnect", "upc", "galaxyclient", "chrome", "firefox", "msedge", "brave",
        "opera", "discord", "spotify", "code", "devenv", "notepad", "widgets", "lockapp",
        "dwm"
    ];

    public static string RunningName => _runningName;

    public static GameProfile Current => _current;

    public static Candidate[] Candidates => _cands;

    public static GameProfile? ByExe(string exe)
    {
        if (string.IsNullOrEmpty(exe))
            return null;

        foreach (GameProfile profile in Known)
        {
            if (string.Equals(profile.Exe, exe, StringComparison.OrdinalIgnoreCase))
                return profile;
        }

        return null;
    }

    private static GameProfile Bind(Binding b, string runningExe)
    {
        GameProfile? profile = ByExe(b.ProfileExe) ?? ByExe(runningExe);
        GameProfile result = (profile ?? Generic).Clone();
        result.Exe = runningExe;

        if (profile == null)
            result.Name = runningExe;

        if (b.Device == Binding.DevVjoy)
            result.NeedsPad = false;
        else if (b.Device == Binding.DevPad)
            result.NeedsPad = true;

        if (b.Device != Binding.DevProfile)
            result.WheelUntested = false;

        return result;
    }

    public static void StartWatching()
    {
        Thread? watcher = _watcher;
        if (watcher != null && watcher.IsAlive)
            return;

        _watcher = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    Detect(force: true);
                }
                catch
                {
                    // a scan failure must never kill the watcher
                }

                Thread.Sleep(2000);
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal
        };
        _watcher.Start();
    }

    private static bool IsShell(string n)
    {
        foreach (string shell in NotGames)
        {
            if (string.Equals(n, shell, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static GameProfile Detect(bool force) => Detect(force, wantCandidates: false);

    /// <summary>
    /// Finds the running title, if any. Bindings win over the built-in table,
    /// and prefix matches cover titles whose executable name varies.
    /// </summary>
    public static GameProfile Detect(bool force, bool wantCandidates)
    {
        if (!force && Environment.TickCount - _checkedAt < 2000)
            return _current;

        _checkedAt = Environment.TickCount;
        GameProfile? found = null;
        var candidates = new List<Candidate>();
        string heaviestExe = "";
        string matchedExe = "";
        long heaviestMem = 0L;
        bool listing = false;

        try
        {
            var byExe = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
            var prefixed = new List<GameProfile>();
            foreach (GameProfile profile in Known)
            {
                byExe.TryAdd(profile.Exe, profile);
                if (!string.IsNullOrEmpty(profile.Prefix))
                    prefixed.Add(profile);
            }

            var bound = new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
            Store? store = Bound;
            if (store != null)
            {
                foreach (Binding binding in store.Bindings)
                {
                    if (!string.IsNullOrEmpty(binding.Exe))
                        bound[binding.Exe] = binding;
                }
            }

            IReadOnlyList<ProcInfo> processes = ProcessSource.List();
            GameProfile? boundMatch = null;
            GameProfile? prefixMatch = null;
            string boundExe = "";
            string prefixExe = "";

            foreach (ProcInfo process in processes)
            {
                string name = process.Name.ToLowerInvariant();

                if (boundMatch == null && bound.TryGetValue(name, out Binding? binding))
                {
                    boundMatch = Bind(binding, name);
                    boundExe = name;
                }

                if (found == null && byExe.TryGetValue(name, out GameProfile? known))
                {
                    found = known;
                    matchedExe = name;
                }

                if (prefixMatch != null)
                    continue;

                foreach (GameProfile profile in prefixed)
                {
                    if (name.StartsWith(profile.Prefix, StringComparison.Ordinal))
                    {
                        prefixMatch = profile;
                        prefixExe = name;
                        break;
                    }
                }
            }

            if (boundMatch != null)
            {
                found = boundMatch;
                matchedExe = boundExe;
            }
            else if (found == null)
            {
                found = prefixMatch;
                matchedExe = prefixExe;
            }

            if (wantCandidates || found == null)
            {
                listing = true;
                foreach (ProcInfo process in processes)
                {
                    if (IsShell(process.Name))
                        continue;

                    candidates.Add(new Candidate
                    {
                        Exe = process.Name,
                        Title = process.Title,
                        Mem = process.Rss
                    });

                    if (process.Rss > heaviestMem)
                    {
                        heaviestMem = process.Rss;
                        heaviestExe = process.Name;
                    }
                }
            }
        }
        catch
        {
            // detection is best effort; the generic profile always applies
        }

        if (listing)
        {
            candidates.Sort((a, b) => b.Mem.CompareTo(a.Mem));
            _cands = candidates.ToArray();
            UnknownExe = found == null ? heaviestExe : "";
        }
        else if (found != null)
        {
            UnknownExe = "";
        }

        _current = found ?? Generic;
        _runningName = found?.Name ?? "";
        LastScanAt = Environment.TickCount;
        MatchedExe = found != null ? matchedExe : "";
        return _current;
    }
}