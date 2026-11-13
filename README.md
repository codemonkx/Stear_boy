# Gyropad PC — Linux port

A community Linux port of **Gyropad PC 1.39** by Sharper Studio, which turns a
phone into a steering wheel for PC sim racing.

Upstream: <https://github.com/BilalShakoor/Gyropad>

## Why this exists

The upstream app ships only for Windows. Its gamepad output depends on two
Windows kernel drivers — ViGEmBus and vJoy — which is the reason a Linux build
has not been produced. Linux has the equivalent capability in the kernel itself
(`uinput` / evdev), so the driver dependency can be removed rather than ported.

## Status

**Analysis complete, port not started.** See [`docs/triage.md`](docs/triage.md)
for the full static analysis of the shipped binary, and
[`docs/plan.md`](docs/plan.md) for the phased port plan and tooling decisions.

Short version:

- The app is 100% managed .NET (CIL-only), 82 types / 555 methods, unobfuscated.
- Every kernel-facing call is isolated in two classes: `ViGEmBridge` and
  `VJoyBridge`.
- Force feedback is not implemented, removing the hardest part of virtual
  gamepad work.
- The phone protocol is plain TCP/UDP, so the existing phone app needs no changes.

## Planned shape

| Windows | Linux |
|---|---|
| `ViGEmBridge` (IOCTL → ViGEmBus) | `UinputBridge` (evdev virtual gamepad) |
| `VJoyBridge` (`vJoyInterface.dll`) | same `UinputBridge` |
| WinForms + hand-painted widgets | Avalonia |
| `netsh advfirewall` | nftables / ufw, or no-op |
| Registry settings | XDG config (`~/.config/gyropad/`) |
| Inno Setup `.exe` | AppImage / `.deb` / tarball |

## Layout

```
docs/        analysis and design notes
reference/   the original Windows build (not committed)
src/         the port
```

## Legal

Upstream publishes binaries only and provides no license, which defaults to
all rights reserved. This is a private porting workspace. The intended outcome
is to hand the work to the original author rather than publish a fork.

## Reference build

Not committed. See [`reference/README.md`](reference/README.md) for the
expected hashes and extraction steps.
