Gyropad 1.39.3 - Linux port
===========================

Turns your phone into a steering wheel or gamepad for PC racing games.

This is the step-by-step guide for the Linux machine. Follow it in order;
each step says what you should see. The app is self-contained: no .NET
install, no kernel driver, no root at runtime. It uses the kernel's uinput
interface, which is already part of Linux.

The only setup step that needs sudo is installing the udev rule once. Do not
run Gyropad itself with sudo; it needs your session's access, not root.

Contents
--------
  0. Which file to download
  1. Extract
  2. Check the machine (--doctor)
  3. Start the app
  4. Connect the phone
  5. Check the virtual devices (the important test)
  6. Try a game
  7. Optional checks
  8. Send the result (--report)
  9. Troubleshooting
 10. Files in this folder
 11. Autostart (optional)
 12. Uninstall


0. Which file to download
-------------------------
On the Linux machine run:

    uname -m

  x86_64   -> gyropad-1.39.3-linux-x64.tar.gz
  aarch64  -> gyropad-1.39.3-linux-arm64.tar.gz

Download it from the project's Releases page. The repository is private, so
sign in first, or copy the file over from another machine.


1. Extract
----------
    mkdir -p ~/Applications
    tar -xzf gyropad-1.39.3-linux-x64.tar.gz -C ~/Applications
    cd ~/Applications/gyropad-1.39.3-linux-x64

Keep this folder: the app always runs from it.

(Use the arm64 tarball name instead if uname -m printed aarch64.)


2. Check the machine
--------------------
    ./Gyropad --doctor

Expected last line:

    Result: ready to run.

The doctor also creates both virtual devices for a moment and destroys them
again, so these two lines are the real proof that the kernel accepts them:

    [ ok ] wheel device  - created and destroyed
    [ ok ] gamepad device  - created and destroyed

If you instead see:

    [fail] device node  - /dev/uinput is missing
    [fail] access  - permission denied ...

install the udev rule once:

    sudo ./install-udev-rule.sh

Then log out and back in (the access rule applies to a local login session)
and run --doctor again. If it still fails, stop here and send a report
(section 8).


3. Start the app
----------------
With a desktop:

    ./Gyropad

A window opens. The pill in the top right says "waiting" until a phone
connects.

Without a desktop, or over SSH:

    ./Gyropad --headless --pair ask

Headless asks on the terminal before accepting a phone. Use --pair auto only
when nobody is there to answer. Stop either mode with Ctrl+C.

Only one copy may run at a time. A second one tells you which process has it.


4. Connect the phone
--------------------
On the phone:
  * open Gyropad,
  * keep it on the same Wi-Fi as the PC,
  * tap Connect,
  * pick this PC from the list.

On the PC:
  * the window shows "<phone> is trying to connect"; press Connect,
  * headless asks "approve? [y/N]" on the terminal.

What good looks like:

  * the pill changes from "waiting" to "wheel" (or "gamepad" if the phone
    switched mode),
  * the PHONE card shows its name and address,
  * the VIRTUAL DEVICES card says "ready",
  * moving a control on the phone updates the numbers in --status.

If nothing appears, see Troubleshooting (section 9).


5. Check the virtual devices (the important test)
-------------------------------------------------
This part has never run on a real Linux kernel before. It is the most
useful thing you can check.

Keep Gyropad running and open a second terminal:

    sudo evtest

Pick the entry named "Gyropad Wheel". Move ONE control at a time on the
phone and note which event changes:

    steering left/right   ABS_X    (-32767 .. 32767, centre 0)
    throttle              ABS_Y
    brake                 ABS_Z
    clutch                ABS_RX
    handbrake             ABS_RY
    buttons               BTN_TRIGGER .. BTN_BASE6, then
                          BTN_TRIGGER_HAPPY1 .. BTN_TRIGGER_HAPPY4
                          (16 buttons in total)

Now switch the phone to gamepad mode, run evtest again, choose
"Microsoft X-Box 360 pad", and check:

    left stick            ABS_X / ABS_Y
    right stick           ABS_RX / ABS_RY
    left trigger          ABS_Z
    right trigger         ABS_RZ
    d-pad                 ABS_HAT0X / ABS_HAT0Y
    A B X Y               BTN_SOUTH, BTN_EAST, BTN_NORTH, BTN_WEST
    LB RB                 BTN_TL, BTN_TR
    back start guide      BTN_SELECT, BTN_START, BTN_MODE
    stick clicks          BTN_THUMBL, BTN_THUMBR

Note anything missing, stuck, reversed, or on the wrong axis. That note plus
the evtest output is the single most useful bug report you can send.

If evtest is not installed:

    sudo apt install evtest        # Debian / Ubuntu
    sudo dnf install evtest        # Fedora
    sudo pacman -S evtest          # Arch

jstest, from the "joystick" package, shows the same thing for /dev/input/jsN.


6. Try a game
-------------
Start Gyropad first, then the game. The game should see a normal controller
and need no driver or setup.

Wheel mode:
    a racing title with wheel support, for example Dirt Rally, Assetto
    Corsa or Euro Truck Simulator 2. If the game does not pick the axes up
    automatically, bind steering, throttle and brake in its own controls
    screen. The axis order matches the Windows build, so existing bindings
    carry over.

Gamepad mode:
    any title that expects an Xbox pad, for example Forza or EA WRC.

Steam and Proton titles usually detect the gamepad through SDL without any
configuration. If Steam does not list it, open Steam's controller settings
while Gyropad is running.

A game that was already open before the device appeared may need a restart.
Gyropad itself does not need restarting.


7. Optional checks
------------------
USB tethering
    Turn on USB tethering on the phone, connect the cable, and pair again.
    The PC should be reachable over the cable with Wi-Fi off. This is useful
    when a Wi-Fi network blocks the phone-to-PC traffic.

Telemetry (Live for Speed)
    Enable OutGauge and OutSim in LFS. Gyropad listens on UDP 30000 and
    30001. The drift assist uses OutSim.

Reconnect
    Put the phone to sleep, wake it, and confirm the link comes back
    without restarting Gyropad. The pill should return to "wheel" or
    "gamepad".

Suspend / resume
    Suspend the PC, resume it, and check that the virtual devices and the
    phone link still work. If they do not, note exactly what you did.

Live status at any time
    ./Gyropad --status
    ./Gyropad --unpair        # forget the paired phone

If the phone cannot reach the PC on a restrictive network, allow inbound
UDP 5005-5006 and TCP 5007-5011 for the session:

    sudo ufw allow 5005:5006/udp
    sudo ufw allow 5007:5011/tcp


8. Send the result
------------------
From the Gyropad folder, run (not with sudo):

    ./Gyropad --report

It writes gyropad-report-YYYYMMDD-HHMMSS.txt in that folder. The file
contains:

  * the --doctor output,
  * the running instance's status and current axis values,
  * your settings file, with the pairing token redacted,
  * the capabilities of the Gyropad input devices,
  * the network interfaces (so USB tethering can be checked).

If the problem happens while the app runs, also capture the session:

    ./Gyropad --headless --verbose 2>&1 | tee gyropad-session.log

Fill in the three lines at the top of the report (what you did, what you
expected, what happened), then send the report, the session log if you have
one, and your evtest notes.


9. Troubleshooting
------------------
doctor says platform [fail]
    Wrong tarball for this CPU; compare uname -m with section 0.

doctor says /dev/uinput is missing
    Load the module with sudo modprobe uinput, then rerun
    install-udev-rule.sh.

doctor says permission denied
    The udev rule is not installed, or this is a remote session. The access
    rule covers local logins; log out and back in, or add your user to the
    group that owns /dev/uinput.

doctor says the kernel rejected the device description (EINVAL)
    This affected versions before 1.39.3, where the device description was
    sent with the wrong field order. Check ./Gyropad --version and use 1.39.3
    or newer. If it still happens, send a report (section 8).

Phone does not appear in the app
    Phone and PC on different networks or VLANs, guest Wi-Fi, or a VPN
    capturing all traffic. UDP 5006 must reach the PC. Try USB tethering.

Phone sees the PC but tapping does nothing
    A firewall is blocking inbound TCP. Allow TCP 5007-5011, or let the
    phone ask this PC to connect back.

App runs but the game sees no device
    Start Gyropad first, then the game. Check evtest; if evtest shows the
    device, the problem is in the game's controller settings.

Steam does not detect the pad
    Check Steam's controller settings while Gyropad is running. The pad is
    an Xbox 360 compatible device.

Steering is reversed in one game
    Invert it in the game's own bindings; the axis sign matches the Windows
    build on purpose.

Window does not open
    No desktop session (SSH or a headless server). Use
    ./Gyropad --headless --pair auto.

Headless declines pairing
    stdin is not a terminal, so --pair ask cannot prompt. Use --pair auto.

A second instance is refused
    Another Gyropad is already running. ./Gyropad --status shows its pid;
    stop it first.

Diagnostics to include with a report:

    ls -l /dev/uinput
    lsmod | grep uinput
    cat /proc/bus/input/devices        # after the app created a device
    sudo evtest                        # pick the Gyropad device


10. Files in this folder
------------------------
Gyropad                  the application; self-contained, no .NET needed
README.txt               this guide
70-gyropad.rules         udev rule that grants access to /dev/uinput
install-udev-rule.sh     installs the rule and loads the module at boot

Settings live in:

    $GYROPAD_CONFIG_DIR/settings.ini       if that variable is set
    $XDG_CONFIG_HOME/gyropad/settings.ini  else
    ~/.config/gyropad/settings.ini         else

The format is the original Windows INI, so a settings file from the Windows
build can be copied there unchanged. It stores the paired phone and token,
the drift-assist tuning, and per-title device bindings.


11. Autostart (optional)
-------------------------
A systemd user service starts the headless link when you log in. Create
~/.config/systemd/user/gyropad.service:

    [Unit]
    Description=Gyropad phone link
    After=network-online.target

    [Service]
    ExecStart=%h/Applications/gyropad-1.39.3-linux-x64/Gyropad --headless --pair auto
    Restart=on-failure

    [Install]
    WantedBy=default.target

Then:

    systemctl --user daemon-reload
    systemctl --user enable --now gyropad
    ./Gyropad --status

A service started at boot cannot use the session-based udev access. Add your
user to the group that owns /dev/uinput (usually input) and log out and back
in:

    sudo usermod -aG input "$USER"


12. Uninstall
-------------
    rm -rf ~/Applications/gyropad-1.39.3-linux-x64
    rm -rf ~/.config/gyropad

And, only if you installed the udev rule:

    sudo rm -f /etc/udev/rules.d/70-gyropad.rules
    sudo rm -f /etc/modules-load.d/gyropad.conf

That is everything; no kernel driver and no service are left behind.
