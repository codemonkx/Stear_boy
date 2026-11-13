# Triage: Gyropad PC 1.39

Static analysis of the shipped Windows build, performed 2026-11-14.
Every claim below comes from the artifact itself, not from documentation.

## Artifact identity

| Field | Value |
|---|---|
| Upstream | `github.com/BilalShakoor/Gyropad` (public repo, binary downloads only) |
| Publisher | Sharper Studio |
| Product version | 1.39 |
| Installer | Inno Setup 6.7.0, x86, single payload |
| App | `GyropadPC.exe`, PE32+, x86-64, 18,033,664 bytes |
| Runtime | .NET Framework 4.0 (`v4.0.30319`), CIL-only |
| UI | WinForms + System.Drawing |
| Obfuscation | none observed |
| Strong name | none |
| Metadata | 82 types, 555 methods, 760 fields, 23 `ImplMap` rows, 5 `ModuleRef` |

SHA-256 of the app assembly: `c23a4194149a53bb822c9b71ba4f75470af2c36a49f268838ebf4103bd28612b`

## Embedded payloads

The assembly carries two installers as resources and runs them on demand:

| Resource | Size | Purpose |
|---|---|---|
| `ViGEmBus.exe` | 6,278,576 | Windows kernel driver — virtual Xbox controller |
| `vJoySetup.exe` | 11,482,272 | Windows kernel driver — virtual joystick |

Both are dropped to disk, executed with `runas`, and the user sees an
`Install …?` prompt first.

## Architecture

The application is a TCP/UDP bridge: a phone acts as the steering wheel, this
process converts that input into a virtual gamepad for the game.

Layers, by type:

| Layer | Types | Notes |
|---|---|---|
| Phone link | `Net`, `PortOwner`, `Discovered`, `Binding` | `TcpListener` / `UdpClient`; `GetExtendedUdpTable` used for port ownership |
| Input model | `Pad`, `Wheel`, `PhoneLayout`, `ModeReadout` | axes, buttons, POV hats |
| Driver output | `ViGEmBridge`, `VJoyBridge` | the only kernel-facing code |
| Games | `Games`, `GameProfile`, `Candidate` | process-name detection, 15+ titles |
| Telemetry | `OutGauge`, `OutSim` | Live for Speed UDP protocols |
| Assist | `DriftAssist` | input shaping |
| Config | `Store` | persistence |
| Drivers | `Drivers`, `Install` | unpacks and runs the embedded installers |
| Network setup | `Firewall` | `HNetCfg.FwPolicy2` + `netsh advfirewall` |
| Updates | `Updater`, `UpdateDot`, `BuildStamp` | self-update |
| UI | `Theme`, `FlatBtn`, `Chip`, `GamePill`, `Card`, `PadDiagram`, `WheelArt`, `Glyph`, `Scroller`, `GlowBtn`, `MainForm`, `ApprovalDialog`, … | ~20 hand-painted widgets |

## Windows-specific surface

Exactly 23 `ImplMap` rows. Grouped:

- **ViGEm**: `SetupDiGetClassDevsW`, `SetupDiEnumDeviceInterfaces`,
  `SetupDiGetDeviceInterfaceDetailW`, `SetupDiDestroyDeviceInfoList`,
  `CreateFileW`, `DeviceIoControl`, `CloseHandle`
  → talks to the driver by **raw IOCTL**, not through `ViGEmClient.dll`.
- **vJoy**: `LoadLibraryW`, `GetProcAddress`, `SetDllDirectoryW`, `FreeLibrary`
  → dynamically binds `vJoyInterface.dll`.
- **Window management**: `SetForegroundWindow`, `ShowWindow`, `IsIconic`,
  `GetWindowRect`, `WindowFromPoint`, `GetAncestor`, `DwmSetWindowAttribute`,
  `ReleaseCapture`, `SendMessage`.
- **Network**: `GetExtendedUdpTable`.

## Port assessment

| Concern | Assessment |
|---|---|
| Logic reuse | High — the driver layer is already isolated behind two bridge classes |
| Phone compatibility | Fully preservable; protocol is C# TCP/UDP on both ends |
| Force feedback | Not implemented (no FFB calls in the vJoy surface) — removes the hardest problem |
| Driver replacement | `uinput` / evdev virtual gamepad; kernel built-in, no install step |
| Anti-cheat | Not a concern for the supported titles under Proton |
| Wine shortcut | Impossible — kernel driver IOCTLs cannot run under Wine |
| Main risk | Device identity (VID/PID/GUID) differs from vJoy/XInput |
| Main effort | ~20 hand-drawn widgets must be redrawn for a cross-platform UI toolkit |

## Legal status

The upstream repository publishes **binaries only** and carries **no LICENSE
file**, which defaults to all rights reserved. This repository is therefore a
private analysis/port workspace. Do not redistribute a derived Linux build
without the author's permission; prefer contributing the port back to him.

## Open questions

- How much WinForms logic is entangled with business logic? (blocks estimate)
- Does the phone protocol depend on Windows-specific handshake details?
- Is `DriftAssist` reading raw HID reports anywhere?
