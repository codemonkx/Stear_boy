# Running Gyropad on Linux

For the step-by-step first-run and testing guide, see
[`../packaging/README-linux.txt`](../packaging/README-linux.txt) (shipped in the
tarball as `README.txt`). This page is the deeper reference: permissions,
device layout, configuration and troubleshooting.

The Linux port replaces the two Windows kernel drivers (vJoy and ViGEmBus) with
the kernel's own `uinput` interface. Nothing else about the app changes: the
phone app, the wire protocol and the settings file stay the same as the Windows
build.

## Requirements

| | |
|---|---|
| Architecture | 64-bit x86 (`linux-x64`) or ARM64 (`linux-arm64`) |
| Desktop | X11 or Wayland for the window; not needed with `--headless` |
| Kernel | `uinput` available (module or built in) |
| Runtime | none - the release tarball is self-contained |

The binaries are published as self-contained .NET 8 apps, so `dotnet` does not
need to be installed to run Gyropad. Building from source does need the .NET 8
SDK.

## Install from a release tarball

```bash
mkdir -p ~/Applications
tar -xzf gyropad-1.39.6-linux-x64.tar.gz -C ~/Applications
cd ~/Applications/gyropad-1.39.6-linux-x64

./Gyropad --doctor          # environment check
sudo ./install-udev-rule.sh # once, only if the doctor reports a permission problem
./Gyropad                   # window
# or, without a desktop session:
./Gyropad --headless --pair auto
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
config directory is writable, and whether a desktop display is present. It also
creates both virtual devices for a moment and destroys them again, which is the
only check that proves the kernel accepts their descriptions. It exits non-zero
when something would stop the app from working.

```
$ ./Gyropad --doctor
Gyropad 1.39.6 doctor

System
  [ ok ] runtime  - .NET 8.0.x
  [ ok ] os  - Ubuntu 24.04.1 LTS
  [ ok ] architecture  - x64
  [ ok ] platform  - Linux

Virtual input (uinput)
  [ ok ] device node  - /dev/uinput
  [ ok ] access  - read/write
  [ ok ] wheel device  - created and destroyed
  [ ok ] gamepad device  - created and destroyed
...
Result: ready to run.
```

The device probe is the check that catches a rejected description (the kernel
answers `EINVAL` at `UI_DEV_SETUP`), so a broken build fails here instead of
later inside a game.

The same checks are available from the app: **Run doctor** in the virtual device
card opens the output in a window (network ports are skipped there because the
running app holds them). That is handy when the app works but a game does not,
because the report is already on screen.

## Headless mode

`--headless` starts the phone link, the `uinput` devices, telemetry and title
detection without opening a window. It needs no X11/Wayland session, so it works
over SSH and on minimal installs.

```bash
./Gyropad --headless                 # approve pairing on the terminal (default)
./Gyropad --headless --pair auto     # accept every pairing request
./Gyropad --headless --pair deny     # refuse every pairing request
./Gyropad --headless --verbose       # print input once a second
```

The default policy is `ask`: a request prints the phone name and waits for
`y`/`n` on the terminal. If stdin is not a terminal (systemd, a pipe, cron),
`ask` declines the request and says so; use `--pair auto` for unattended runs.

While a session runs it publishes `runtime.json` next to `settings.ini`. Two
one-shot commands read or edit that state:

```bash
./Gyropad --status     # pid, uptime, link, ports, device, live input; exits 1 if none is running
./Gyropad --unpair     # clears the stored phone and token
```

`--status` exits non-zero and explains that the file is stale when the pid is
gone, so it is safe in scripts. `--unpair` warns when an instance is still
running, because that process keeps the old token in memory until it restarts.

Ctrl+C, `SIGINT`, `SIGTERM` and `SIGHUP` stop the session cleanly: the outputs
are zeroed and `runtime.json` is removed. Only one instance (window or headless)
may run per settings directory; a second one exits with the pid of the first.
The lock is released by the kernel if a process crashes.

### Autostart with systemd

```ini
# ~/.config/systemd/user/gyropad.service
[Unit]
Description=Gyropad phone link
After=network-online.target

[Service]
ExecStart=%h/Applications/gyropad-1.39.6-linux-x64/Gyropad --headless --pair auto
Restart=on-failure

[Install]
WantedBy=default.target
```

```bash
systemctl --user daemon-reload
systemctl --user enable --now gyropad
./Gyropad --status
```

The packaged udev rule grants `/dev/uinput` through `uaccess`, which only applies
to a local login session. For a service that starts at boot, add your user to the
group that owns the node (usually `input`) instead, then log out and back in:

```bash
sudo usermod -aG input "$USER"
```

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

Racepad is a wheel, so its device is a joystick rather than an Xbox
controller. If a Racepad control does nothing in a game, run `--status` while
moving it: the `packets` line proves the frames arrive, and the `route` line
names the device the game must be pointed at.

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
| `Gyropad could not open a window` | no desktop session (SSH, headless server); use `./Gyropad --headless --pair auto` |
| pairing is declined in headless mode | stdin is not a terminal, so `--pair ask` cannot prompt; use `--pair auto` |
| gamepad is not detected by Steam | make sure the title is launched after Gyropad created the device; check `evtest` or `jstest` |
| Racepad does nothing in a game | run `--status` and read `route` and `packets`; bind the `Gyropad Wheel` joystick in the game, or use a title that gets the gamepad persona |
| analog sticks were upside down | fixed in 1.39.6; Linux counts the vertical axis downward, so both stick Y axes are flipped now |
| steering is inverted in one game | invert it in the game's own bindings; the steering axis sign matches vJoy on Windows |

Useful diagnostics:

```bash
ls -l /dev/uinput
lsmod | grep uinput
cat /proc/bus/input/devices        # after the app created a device
sudo evtest                        # pick the Gyropad device
```

## Sending a bug report

Run this on the machine that has the problem, from the folder where Gyropad is
installed (not with `sudo`, so the file belongs to you):

```bash
./Gyropad --report
```

It writes `gyropad-report-YYYYMMDD-HHMMSS.txt` next to the binary. The file
contains the doctor output, the running instance's status (`--status`), the
settings file with the pairing token redacted, the capabilities of the Gyropad
`/sys/class/input` devices, and the network interfaces (useful for USB
tethering). Fill in the three lines at the top - what you did, what you
expected, what happened - and send the whole file.

It is safe to run while Gyropad is running; port checks are skipped in that
case. If the problem is a wrong axis or button, also run `sudo evtest`, pick the
`Gyropad Wheel` or `Microsoft X-Box 360 pad` entry, move one control at a time
and include that output.

## Building from source

```bash
dotnet build Gyropad.sln
dotnet test Gyropad.sln
dotnet run --project src/Gyropad.Linux
dotnet run --project src/Gyropad.Linux -- --headless --pair auto
```

Release tarballs:

```bash
bash packaging/package.sh                     # both architectures
bash packaging/package.sh linux-x64           # one
```

Output lands in `dist/` with a `SHA256SUMS` file.
