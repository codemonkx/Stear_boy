# Gyropad PC — Linux port

A community Linux port of **Gyropad PC 1.39** by Sharper Studio, which turns a
phone into a steering wheel or gamepad for PC racing games.

Upstream: <https://github.com/BilalShakoor/Gyropad>

## Why this exists

The upstream app ships only for Windows. Its gamepad output depends on two
Windows kernel drivers — ViGEmBus and vJoy — which is the reason a Linux build
has not been produced. Linux has the equivalent capability in the kernel itself
(`uinput` / evdev), so the driver dependency can be removed rather than ported.

## Status

The core and the desktop app are ported and the first release artifacts build:

- phone discovery, pairing, token verification and the full 2.x input protocol;
- wheel and gamepad personas on `uinput`, including the per-title routing and
  drift assist;
- OutGauge/OutSim telemetry, Linux game detection, and the original settings
  file format;
- an Avalonia desktop window with pairing approval, device status, discovered
  phones and a built-in environment check (`--doctor`);
- a headless CLI mode for SSH and minimal installs: `--headless`, `--pair`,
  `--status`, `--unpair` and `--verbose`;
- unit and loopback integration tests (72) that run on any OS;
- self-contained `linux-x64` and `linux-arm64` tarballs built by CI.

Runtime behaviour on a real Linux kernel is verified on the test machine of the
port's author, not in this repository's CI, which has no uinput device. See
[`docs/linux-setup.md`](docs/linux-setup.md) for installation and troubleshooting.

## Install

Download a release tarball and follow
[`docs/linux-setup.md`](docs/linux-setup.md):

```bash
tar -xzf gyropad-1.39.1-linux-x64.tar.gz -C ~/Applications
cd ~/Applications/gyropad-1.39.1-linux-x64
./Gyropad --doctor
./Gyropad
```

The app is self-contained. If the doctor reports a permission problem, run
`sudo ./install-udev-rule.sh` once.

## Run

The window needs a desktop session:

```bash
./Gyropad                 # window with pairing approval and device status
./Gyropad --doctor        # environment check, exits non-zero on problems
```

Headless mode runs the same phone link and virtual devices without Avalonia, for
SSH, a minimal install or an autostart service:

```bash
./Gyropad --headless --pair ask   # approve each pairing request on the terminal
./Gyropad --headless --pair auto  # accept pairing requests unattended
./Gyropad --headless --verbose    # print live input once a second

./Gyropad --status                # inspect the running instance
./Gyropad --unpair                # forget the paired phone
```

Ctrl+C stops a headless session cleanly. `--pair ask` needs a terminal; with
redirected input it declines and points at `--pair auto`. A second instance is
refused, and `--status` reports the pid and uptime of the one that is running.

## Build from source

Requires the .NET 8 SDK.

```bash
dotnet build Gyropad.sln
dotnet test Gyropad.sln
dotnet run --project src/Gyropad.Linux -- --doctor
dotnet run --project src/Gyropad.Linux
dotnet run --project src/Gyropad.Linux -- --headless --pair auto
```

Release tarballs (`dist/`, with checksums):

```bash
bash packaging/package.sh
```

## Docs

- [`docs/linux-setup.md`](docs/linux-setup.md) — installation, permissions, games, troubleshooting.
- [`docs/triage.md`](docs/triage.md) — static analysis of the shipped Windows binary.
- [`docs/code-map.md`](docs/code-map.md) — every decompiled type with a port verdict.
- [`docs/protocol.md`](docs/protocol.md) — the phone protocol, reconstructed.
- [`docs/plan.md`](docs/plan.md) — phased port plan and tooling decisions.

## Layout

```
src/Gyropad.Core/    protocol, input model, config, telemetry, phone link, uinput
src/Gyropad.Linux/   Avalonia app, headless CLI, --doctor
tests/               xUnit tests (ABI, mappings, loopback phone link)
packaging/           udev rule, installer, tarball script
docs/                analysis, design and setup notes
reference/           the original Windows build (not committed)
```

## Legal

Upstream publishes binaries only and provides no license, which defaults to
all rights reserved. This is a private porting workspace. The intended outcome
is to hand the work to the original author rather than publish a fork.

## Reference build

Not committed. See [`reference/README.md`](reference/README.md) for the
expected hashes and extraction steps.
