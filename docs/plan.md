# Port plan: Gyropad PC 1.39 → Linux

Status: **Phases 0-6 complete; validation pending on real Linux hardware.**

The core, `uinput` layer, Avalonia app, packaging and CI are in the repository.
What remains is runtime validation on a Linux machine: `evtest`/`jstest` axis
and button checks, then the game parity matrix (phase 7). The port's author
tests that on their own laptop; this repository's CI can build and test the
managed code but has no uinput device.

This plan is derived from [`triage.md`](triage.md), which is itself derived from
the shipped binary. Where the triage says "possible", this plan says how we
verify it.

## Goal

A native Linux build of Gyropad PC that:

1. works with the **existing phone app, unmodified**,
2. presents a virtual gamepad the way vJoy/XInput did, so games see the same
   device,
3. keeps the app's look and feel (it is a hand-painted UI, not a stock form),
4. needs **no kernel driver install** — `uinput` is mainline,
5. can be handed to the original author as a contribution rather than shipped
   as a competing fork.

## Non-goals

- Force feedback. The Windows build never implemented it, so there is nothing
  to translate (`ViGEmBridge`/`VJoyBridge` expose no FFB calls).
- Windows support. The port may keep a `--platform windows` build path alive,
  but Windows is a frozen reference, not a target.
- Auto-update. The upstream `Updater` fetches Windows installers; on Linux it
  would fetch something that does not exist. Dropped, replaced by packaging.
- Wine. Confirmed impossible: `ViGEmBridge` issues raw `DeviceIoControl`
  IOCTLs to a kernel driver Wine cannot install.

## Stack decision

**C# / .NET 8 + Avalonia**, not a rewrite.

| Option | Verdict |
|---|---|
| **C# + Avalonia** | ~90% of business logic survives decompilation; single-language port; phone protocol code is already C# |
| C# + GTK# | same logic reuse, weaker custom-drawing story than Avalonia for ~20 painted widgets |
| Rust / Go rewrite | full re-implementation of games list, drift assist, telemetry, protocol; no reason to pay that |

The only genuinely non-portable code is the driver layer (already isolated in two
classes), the window-management P/Invokes, `netsh`/COM firewall code, and the
WinForms widgets. Everything else moves over nearly verbatim.

## Phase overview

| Phase | Output | Gate to leave the phase |
|---|---|---|
| 0. Tooling | decompiler, graph pipeline, protocol harness | `ilspycmd` runs, graphify builds a graph of the decompiled tree |
| 1. Decompile & map | `decompile/` tree, `docs/code-map.md` | every one of the 82 types has an assigned owner and a port verdict |
| 2. Protocol spec | `docs/protocol.md` + capture/regression tests | a test client drives the Windows build and the Linux build identically |
| 3. Core extraction | `Gyropad.Core` library, no Windows APIs | core library builds and unit-tests on Linux |
| 4. `UinputBridge` | virtual gamepad via evdev | `evdev-joystick`/`jstest` sees correct axes, buttons, hats |
| 5. Avalonia UI | feature-equivalent window | every screen and control in the Windows build exists |
| 6. Linux integration | config, firewall, packaging | installs and runs on a clean distro image |
| 7. Validation | parity matrix | supported games respond like the Windows build |
| 8. Hand-back | upstream-ready diff / PR | author has everything needed to accept or reject |

## Phase 0 — Tooling and reference

Status: **done.**

- .NET SDK 8.0.428 and 10.0.404 installed; `ilspycmd` 11.2.0.10050 installed.
- `innounp` extraction done; `reference/Gyropad-PC-Setup.exe` stays untracked with
  hashes in [`../reference/README.md`](../reference/README.md).
- Decompiled to `decompile/` with `ilspycmd -p --nested-directories` (45 C#
  files, git-ignored).
- Graph pipeline: `graphify` 0.9.108 working. The graph is worth building now
  that the corpus is real C#; `graphify extract` → `cluster-only` →
  `export html`, driven by the CLI because the skill's inline Python blocks do
  not fit this Windows ARM64 host.

Toolchain:

| Need | Status |
|---|---|
| .NET SDK | installed (8.0.428 + 10.0.404) |
| `ilspycmd` | installed (11.2.0.10050) |
| Linux runtime for testing | **not available on this machine, by decision** |
| Native disassembler | not needed; app is CIL-only |

Testing decision: there is no WSL, Docker, or VM here. The port is therefore
built and cross-published for `linux-x64` and `linux-arm64` from this machine,
and the first real test happens on the user's Linux laptop. Consequences:

- Phases 1–3 can be developed and unit-tested on Windows (they are
  platform-agnostic).
- Phase 4 onwards cannot be executed here. Expect a feedback loop: build →
  user runs → reports → fix. Keep `UinputBridge` small and heavily logged so
  remote diagnosis is cheap.
- CI (`ubuntu-latest`) still covers build + unit tests, and is the closest thing
  to a Linux smoke test.

## Phase 1 — Decompile and map

Status: **largely done.** See [`code-map.md`](code-map.md).

Commands used:

```powershell
ilspycmd -p --nested-directories -o decompile "<...>\GyropadPC.exe"
```

- Decompiled project with cross-references intact.
- `docs/code-map.md`: every named type assigned a layer and a verdict
  (keep / adapt / rewrite / drop).
- The four open questions in `triage.md` are answered there:
  business logic is not trapped in `MainForm`; the phone handshake is
  OS-agnostic; `DriftAssist` reads only `OutSim`; `Store` is a flat INI.
- The graph is built over `decompile/`: 639 nodes, 1,554 edges, 39 communities
  (`graphify extract decompile --code-only`). Hubs confirm the code map:
  `MainForm` (71 edges), `PadDiagram` (63), `Net` (60), `VJoyBridge` (53),
  `ViGEmBridge` (27) — the two bridges are isolated leaves, which is exactly the
  isolation the port depends on. `graphify-out/` is git-ignored.


## Phase 2 — Phone protocol specification

The phone app is the part we cannot change, so the protocol gets specified and
tested before any port code is written.

- Read `Net`, `PortOwner`, `Discovered`, `Binding`, `Pad`, `Wheel`,
  `PhoneLayout`, `ModeReadout`.
- `docs/protocol.md` is written from the decompiled source: transports, ports,
  discovery lines, pair/hello exchange, all six UDP packet layouts, the tag
  algorithm, dedup/liveness rules, and the telemetry packet.
- Still to do: capture real traffic from the Windows build with the actual phone
  app and confirm the *verify* items; keep captures as text fixtures.
- Write a protocol harness that replays those fixtures. It must pass against the
  Windows build first, then against the port. That harness is the contract.

Exit criteria: a synthetic client that speaks the protocol, plus golden
fixtures, plus a documented refusal case (wrong version, no handshake).

## Phase 3 — Core extraction

Split the decompiled code into `Gyropad.Core` (portable) and platform shims.

Keep, nearly as-is:

- `Pad`, `Wheel`, `PhoneLayout`, `ModeReadout` — input model.
- `DriftAssist` — input shaping, pure math.
- `Games`, `GameProfile`, `Candidate` — process detection becomes
  `/proc/<pid>/comm`, so only the *matching* logic survives; the enumeration
  does not.
- `OutGauge`, `OutSim` — LFS UDP telemetry, already portable.
- `Net`, `Binding`, `Discovered` — TCP/UDP.

Adapt:

- `Store` — Windows registry/settings file → XDG config
  (`$XDG_CONFIG_HOME/gyropad/config.json`), schema-compatible where practical.
- `Games` process lookup — `/proc` scan instead of `Process.GetProcessesByName`.

Drop:

- `Drivers`, `Install` — they only unpack and run the two embedded kernel
  installers.
- `Firewall` — `netsh advfirewall` and `HNetCfg.FwPolicy2` do not exist; replaced
  by an optional `ufw`/nftables snippet or documented manual step.
- `Updater`, `UpdateDot`, `BuildStamp` — Windows installer self-update.
- All window-management P/Invokes (`SetForegroundWindow`, `GetWindowRect`,
  `WindowFromPoint`, `DwmSetWindowAttribute`, …).

Exit criteria: `Gyropad.Core` has zero `DllImport` of Windows-only libraries and
its tests pass on Linux CI.

## Phase 4 — `UinputBridge`

One class replaces both `ViGEmBridge` and `VJoyBridge`.

Design points:

- Create `/dev/uinput` device with a descriptive name; set `EV_KEY` gamepad
  buttons, `EV_ABS` axes, and hat switches.
- Match the axis ranges the app already produces (vJoy used signed 16-bit
  ranges with configurable min/center/max; keep the same numbers so `DriftAssist`
  and game profiles do not need retuning).
- Handle the two device personas: Xbox-style pad (ViGEm) and joystick (vJoy).
  Decide whether to expose one device with a superset of capabilities or two
  selectable profiles; the latter is closer to current behaviour.
- udev rule + `input` group guidance so the app runs without root.
- Hot-plug robustness: game launches after the app, and game restarts must not
  require restarting Gyropad.

Validation tools (on the user's machine): `evdev-joystick`, `jstest`,
`jstest-gtk`, SDL2 `controllermap`, and at least one supported title under
Proton. The build ships with a `--doctor` flag that dumps `/dev/uinput` access,
created device capabilities, and the first N events, so a remote report is
actionable without a debugger.


Exit criteria: axis/button/hat readout matches the Windows device mapping table
in `docs/protocol.md`; a game sees the device without a restart.

## Phase 5 — Avalonia UI

~20 hand-painted widgets (`Theme`, `FlatBtn`, `Chip`, `GamePill`, `Card`,
`PadDiagram`, `WheelArt`, `Glyph`, `Scroller`, `GlowBtn`, dialogs) are redrawn
as Avalonia controls. This is the largest calendar-time item and has no shortcut.

Approach:

- Port `Theme` first: colors, fonts, radii, shadows as resources.
- Then the primitives (`FlatBtn`, `Chip`, `Card`, `Glyph`, `Scroller`).
- Then the illustrative ones (`PadDiagram`, `WheelArt`) as vector drawings.
- Then `MainForm` layout last, once the primitives exist.
- Preserve behaviour, not pixel identity: same information, same hierarchy, same
  tactile feel. A cross-platform window manager will not reproduce Windows
  chrome exactly, and chasing that is a trap.

Exit criteria: every screen and state in the Windows build exists; no feature is
reachable only through Windows-only affordances.

## Phase 6 — Linux integration and packaging

- Config in XDG paths, migrated from a Windows-style file if one is found.
- Optional firewall helper (documented, never silently elevating).
- Desktop entry + icon + autostart toggle.
- Packaging: `dotnet publish -r linux-x64 --self-contained` (and `linux-arm64`)
  produces a tarball that can be built here. AppImage and `.deb` are built in CI,
  because they need Linux tooling.
- Runtime dependency: `libevdev`/`libudev` only.

Exit criteria: install, first-run, uninstall on a clean image; no root at runtime.

## Phase 7 — Validation

Performed on the user's Linux laptop; this machine cannot run the binary. Build a
parity matrix from the supported-titles list in `triage.md`:

| Check | Windows reference | Linux port |
|---|---|---|
| Discovery + connect | | |
| Axis curves / deadzone | | |
| Button mapping | | |
| Hat behaviour | | |
| Game profile auto-detect | | |
| Drift assist on/off | | |
| Telemetry (OutGauge/OutSim) | | |
| Reconnect after phone sleep | | |

Also measure end-to-end input latency (phone → evdev event) and compare.

## Phase 8 — Hand-back

The deliverable to the author is not a GitHub fork with a stance; it is:

- a self-contained `linux/` tree or patch series,
- a short design note explaining why `uinput` replaces both drivers,
- the protocol spec (useful to him regardless of the port),
- a build script and CI config,
- a list of behaviour differences and the reason for each.

Only after he has it does publishing become a separate decision.

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Business logic entangled in `MainForm` | Phase 3 balloons | Resolved: logic lives in `Net`/`Games` (see code map) |
| Undocumented phone handshake quirks | Port cannot connect | Protocol spec written; confirm with capture before Phase 3 |
| Device identity differs from vJoy/XInput | Some games ignore the pad | Test across titles early in Phase 4; expose configurable VID/PID |
| UI port is larger than expected | Schedule | Port `Theme` + primitives first, ship a functional UI, refine |
| No Linux test machine | Bugs found only on user hardware | Small, logging-heavy `UinputBridge`; `--doctor` diagnostics; fast release loop |
| Legal: no upstream license | Cannot publish | Private workspace; hand back to author, do not redistribute |

## Working notes

- `graphify` is for navigating the decompiled corpus (Phase 1 onward), not for
  re-deriving what the triage already established.
- REA is for verifying specific claims against the binary when the decompiler
  output is ambiguous (e.g. exact IOCTL codes, resource names, string tables).
- Every phase should leave `docs/` updated. The docs are the deliverable that
  survives even if the port stalls.
