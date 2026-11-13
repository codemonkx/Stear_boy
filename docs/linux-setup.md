# Running Gyropad on Linux

The Linux port replaces the two Windows kernel drivers (vJoy and ViGEmBus) with
the kernel's own `uinput` interface. Nothing else about the app changes: the
phone app, the wire protocol and the settings file stay the same as the Windows
build.

## Requirements

| | |
|---|---|
| Architecture | 64-bit x86 (`linux-x64`) or ARM64 (`linux-arm64`) |
| Desktop | X11 or Wayland session |
| Kernel | `uinput` available (module or built in) |
| Runtime | none - the release tarball is self-contained |

The binaries are published as self-contained .NET 8 apps, so `dotnet` does not
need to be installed to run Gyropad. Building from source does need the .NET 8
SDK.

## Install from a release tarball

```bash
mkdir -p ~/Applications
tar -xzf gyropad-1.39.0-linux-x64.tar.gz -C ~/Applications
cd ~/Applications/gyropad-1.39.0-linux-x64

./Gyropad --doctor          # environment check
sudo ./install-udev-rule.sh # once, only if the doctor reports a permission problem
./Gyropad
```

`install-udev-rule.sh` does three things:

1. `modprobe uinput` for the current boot.
2. Writes `/etc/modules-load.d/gyropad.conf` so the module loads at boot.
3. Installs `70-gyropad.rules` into `/etc/udev/rules.d/`.

The rule is:

```
KERNEL=="uinput", SUBSYSTEM=="misc", TAG+="uaccess", OPTIONS+="static_node=uinput"
```

`TAG+="uaccess"` hands `/dev/uinput` to the user of the active local session
through systemd-logind, so no `input` group membership is needed and the app is
never run as root. On distributions without logind, add yourself to the group
that owns the node (usually `input`) instead:

```bash
sudo usermod -aG input "$USER"   # log out and back in
```

## Checking a machine

`./Gyropad --doctor` prints the runtime, kernel and architecture, whether
`/dev/uinput` exists and is writable, whether the module is loaded, whether the
Gyropad ports are free (with the owning process when one is found), whether the
config directory is writable, and whether a desktop display is present. It exits
non-zero when something would stop the app from working.

```
$ ./Gyropad --doctor
Gyropad 1.39.0 doctor

System
  [ ok ] runtime  - .NET 8.0.x
  [ ok ] os  - Ubuntu 24.04.1 LTS
  [ ok ] architecture  - x64
  [ ok ] platform  - Linux

Virtual input (uinput)
  [ ok ] device node  - /dev/uinput
  [ ok ] access  - read/write
...
Result: ready to run.
```

The same checks are available from the app: **Run doctor** in the virtual device
card opens the output in a window. That is handy when the app works but a game
does not, because the report is already on screen.

## What the games see

Gyropad creates two virtual devices through `uinput`:

| Phone mode | Linux device | Identity |
|---|---|---|
| Wheel | generic joystick | `Gyropad Wheel` (`1209:0001`) |
| Gamepad | Xbox 360 compatible | `Microsoft X-Box 360 pad` (`045e:028e`) |

The wheel device keeps vJoy's axis order from the Windows build: X steer
(signed), Y throttle, Z brake, Rx clutch, Ry handbrake, and sixteen buttons.
Existing bindings in sim titles therefore translate directly.

The gamepad device uses the kernel `xpad` key order (A, B, X, Y, LB, RB, back,
start, guide, L3, R3) with two triggers and a hat, which is the layout SDL's
built-in Xbox mapping expects. Steam, Proton and SDL-based titles detect it as a
normal wired Xbox 360 controller.

One deliberate difference from Windows: when a title is on the built-in
"needs a gamepad" list (for example Forza), the wheel input is translated onto
the gamepad device. Windows fed vJoy, which some titles ignore; on Linux the
game gets the controller it asks for.

## Configuration

Settings live in:

```
$GYROPAD_CONFIG_DIR/settings.ini       if the variable is set
$XDG_CONFIG_HOME/gyropad/settings.ini   else
~/.config/gyropad/settings.ini          else
```

The format is the original flat INI, so a settings file copied from the Windows
build works unchanged. It stores the paired phone and token, assist strength and
drift-assist tuning, and per-title device bindings. Settings sent by the phone
(lock degrees, steering deadzone, lock-to-lock time) stay in memory only, which
matches the Windows build.

## Firewall

There is no `netsh` equivalent here and the app does not try to change firewall
rules. If the phone cannot find the PC, allow inbound UDP 5005 and 5006 and TCP
5007-5011 for the session:

```bash
sudo ufw allow 5005:5011/udp
sudo ufw allow 5007:5011/tcp
```

Phones on the same Wi-Fi normally need no rule. Flatpak/Snap sandboxes and
"block all incoming" firewall profiles do.

If the phone can be seen but cannot connect back, it sends `PW2 PAIRME` and
Gyropad offers to connect to it instead. That path uses ordinary outbound TCP
and usually works when inbound traffic is blocked.

## Troubleshooting

| Symptom | Check |
|---|---|
| `uinput is not available` | `./Gyropad --doctor`; run `install-udev-rule.sh` |
| permission denied on `/dev/uinput` | udev rule missing, or your session is remote (uaccess only covers local sessions) |
| phone not discovered | UDP 5006 blocked, phone and PC on different VLANs/guest Wi-Fi, or a VPN capturing all traffic |
| app starts but no device in game | run the doctor; some titles need a restart after the device appears |
| gamepad is not detected by Steam | make sure the title is launched after Gyropad created the device; check `evtest` or `jstest` |
| steering is inverted in one game | invert it in the game's own bindings; the axis sign matches vJoy on Windows |

Useful diagnostics:

```bash
ls -l /dev/uinput
lsmod | grep uinput
cat /proc/bus/input/devices        # after the app created a device
sudo evtest                        # pick the Gyropad device
```

## Building from source

```bash
dotnet build Gyropad.sln
dotnet test Gyropad.sln
dotnet run --project src/Gyropad.Linux
```

Release tarballs:

```bash
bash packaging/package.sh                     # both architectures
bash packaging/package.sh linux-x64           # one
```

Output lands in `dist/` with a `SHA256SUMS` file.
