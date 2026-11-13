# Code map: decompiled Gyropad PC 1.39

Produced from the shipped assembly with `ilspycmd 11.2.0.10050` (ILSpy 11.2).
Input: `GyropadPC.exe`, SHA-256 `c23a41…28612b`.
Output: `decompile/` (git-ignored) — 45 C# files, namespace `Gyropad`.

Verdicts: **keep** (moves over nearly unchanged), **adapt** (logic survives, platform
calls change), **rewrite** (UI toolkit change), **drop** (Windows-only, no Linux
equivalent needed).

## Portable core

| File | Type(s) | Purpose | Verdict |
|---|---|---|---|
| `P.cs` | `P` (static) | Protocol constants: ports, magic bytes, packet lengths, button/pedal/haptic ids | keep |
| `Pad.cs` | `Pad` | Six axes (raw ±32767 / 0–65535), 16 buttons, hat (8 = neutral) | keep |
| `Wheel.cs` | `Wheel` | Steer/throttle/brake/clutch/handbrake, buttons, rate counters, look axes | keep |
| `Net.cs` | `Net` | Discovery, pairing, UDP input decode, tag verification, routing, telemetry out | adapt |
| `DriftAssist.cs` | `DriftAssist` | Counter-steer assist from OutSim slip angle; pure math | keep |
| `OutGauge.cs` | `OutGauge` | Live for Speed OutGauge listener, UDP 30000 | keep |
| `OutSim.cs` | `OutSim` | Live for Speed OutSim listener, UDP 30001; derives slip and yaw rate | keep |
| `Games.cs` | `Games`, `Candidate` | Supported-title table, process scan, profile selection | adapt |
| `GameProfile.cs` | `GameProfile` | Per-title capability flags (needs pad, pedal map, face map) | keep |
| `Store.cs` | `Store` | INI settings at `%APPDATA%\PhoneWheel\settings.ini` | adapt |
| `Binding.cs` | `Binding` | Per-exe device binding (profile / vJoy / pad) | keep |
| `Discovered.cs` | `Discovered` | A phone seen on the network | keep |
| `PhoneLayout.cs` | `PhoneLayout` (static) | Phone-reported layout state (aspect, face mappings) shared with UI | keep |

`Net.cs` is the only core file with Windows-only dependencies:

- `PortOwner.Describe()` (Windows IP Helper API) for the "port busy" message — drop,
  replace with a plain message or `/proc/net/udp` lookup.
- `NetworkInterface` heuristics for USB tethering (`RNDIS`, `NCM`, "Remote NDIS") —
  adapt to Linux interface names (`usb0`, `enx…`, `rndis0`).
- `BestV6()` / `BroadcastTargets()` — keep, they are plain .NET networking.

## Output layer (replaced)

| File | Type(s) | Windows mechanism | Verdict |
|---|---|---|---|
| `ViGEmBridge.cs` | `ViGEmBridge`, `SP_DEVICE_INTERFACE_DATA` | SetupAPI enumeration + raw `DeviceIoControl` to ViGEmBus; Xbox 360 report (20 bytes) | drop → `UinputBridge` |
| `VJoyBridge.cs` | `VJoyBridge` | `LoadLibraryW("vJoyInterface.dll")` + delegates; 5 axes, 16 buttons, POV | drop → `UinputBridge` |

Device mapping to preserve (`VJoyBridge` lines 374–439):

| Gyropad value | vJoy wheel mode | vJoy pad mode | ViGEm Xbox |
|---|---|---|---|
| steer | X (signed, center 16384) | — | — |
| throttle | Y | — | — |
| brake | Z | — | — |
| clutch | Rx | — | — |
| handbrake | Ry | — | — |
| left stick | — | X / Y (signed) | LX / LY |
| right stick | — | Rx / Ry (signed) | RX / RY |
| LT / RT | — | Z / RZ (0–32768) | 0–255 triggers |
| buttons | 1–16 | 1–16 | 16-bit mask |
| hat | POV 1, ×4500 | POV 1, ×4500 | 4-bit DPAD |

vJoy clamps analog values to `1..32768` (`U()`); ViGEm maps signed axes to
`-32768..32767` and triggers to `0..255`.

## Windows integration (dropped)

| File | Type(s) | What it does | Verdict |
|---|---|---|---|
| `Drivers.cs` | `Drivers` | Unpacks and runs `ViGEmBus.exe` / `vJoySetup.exe` from embedded resources | drop |
| `Install.cs` | `Install` | Installer orchestration and prompts | drop |
| `Firewall.cs` | `Firewall`, `Result`, `State` | `netsh advfirewall` + `HNetCfg.FwPolicy2` COM | drop (optional ufw/nft helper) |
| `Updater.cs` | `Updater`, `Stage` | Downloads and runs new Windows installer | drop |
| `BuildStamp.cs` | `BuildStamp` | Build date constant | drop |
| `PortOwner.cs` | `PortOwner` | `GetExtendedUdpTable` to name the process holding a port | adapt (proc scan) |
| `Program.cs` | `Program` | Single-instance mutex, raise existing window, WinForms startup | adapt |
| `Properties/AssemblyInfo.cs` | — | Assembly metadata | replace |

## UI layer (rewritten in Avalonia)

WinForms + `System.Drawing`; every widget is hand-painted. `MainForm` also owns
some platform workflows (driver install prompt, firewall prompt, update check,
window raise), which move to Linux equivalents or disappear.

| File | Type(s) | Kind | Notes |
|---|---|---|---|
| `MainForm.cs` | `MainForm`, `WRect`, `WPoint` | main window | 41 KB; layout, status, device list, driver/update prompts |
| `Theme.cs` | `Theme` | palette/metrics | port first — everything depends on it |
| `FlatBtn.cs` | `FlatBtn` | button | |
| `GlowBtn.cs` | `GlowBtn` | button | |
| `MethodBtn.cs` | `MethodBtn` | segmented control | |
| `Chip.cs` | `Chip` | tag | |
| `Card.cs` | `Card` | panel | |
| `GamePill.cs` | `GamePill` | game badge | |
| `Glyph.cs` | `Glyph` | icon drawing | vector paths in code |
| `Mark.cs` | `Mark` (static) | drawing helpers | |
| `Lbl.cs` | `Lbl` (static) | label factory | |
| `Scroller.cs` | `Scroller` | custom scrollbar | |
| `PadDiagram.cs` | `PadDiagram` | controller illustration | 32 KB of GDI+ drawing |
| `WheelArt.cs` | `WheelArt` | wheel illustration | 31 KB of GDI+ drawing |
| `ModeReadout.cs` | `ModeReadout`, `Device` | live input readout | |
| `UpdateDot.cs` | `UpdateDot` | update indicator | drop with updater |
| `Picker.cs` | `Picker` | file/exe picker dialog | |
| `Ask.cs` | `Ask` | prompt dialog | |
| `ApprovalDialog.cs` | `ApprovalDialog` | pairing approval | |
| `Press.cs` | `Press`, `Pulse` | press animation helper | |
| `Hot.cs` | `Hot` | hover/hot state helper | |
| `Place.cs` | `Place` | layout coordinates | |

## Answers to the open questions in `triage.md`

1. **How much logic is in `MainForm`?**
   Low. It is layout, status text, and Windows-only prompts
   (`OfferDrivers`, `OfferFirewall`, `OnUpdateClick`, window raising). The core
   state machine is in `Net.Route()` and `Games`. This makes the UI rewrite
   independent of the port.
2. **Does the phone handshake carry Windows-specific details?**
   No. It is ASCII lines over TCP plus a 16-byte random token used as a SHA-256
   prefix for UDP packet tags. Nothing OS-specific. Full spec in
   [`protocol.md`](protocol.md).
3. **Does `DriftAssist` read raw HID?**
   No. It only reads `OutSim.SlipDeg` and the steering value, and returns a
   shaped value. Pure math, portable as-is.
4. **What does `Store` persist?**
   A flat INI at `%APPDATA%\PhoneWheel\settings.ini`: peer name/host/token, ports,
   assist tuning, lock degrees, deadzone, per-exe bindings. Only the path needs
   to change to XDG config.

## Type count

53 named type declarations across 45 files (the assembly's 82 metadata types
include compiler-generated closures, display classes, and nested types).
