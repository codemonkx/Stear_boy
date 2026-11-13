Gyropad 1.39 - Linux port
=========================

Turns your phone into a steering wheel or gamepad for PC racing games.

Requirements
------------
* A 64-bit Linux desktop (X11 or Wayland), glibc 2.31 or newer.
* Kernel support for uinput. Most distributions ship it as a module.

Quick start
-----------
1. Extract this folder somewhere permanent, for example:

       mkdir -p ~/Applications
       tar -xzf gyropad-1.39.0-linux-x64.tar.gz -C ~/Applications
       cd ~/Applications/gyropad-1.39.0-linux-x64

2. Check the environment:

       ./Gyropad --doctor

3. If the doctor reports a permission problem, install the udev rule once:

       sudo ./install-udev-rule.sh

   Then run the doctor again and start the app:

       ./Gyropad

4. Open Gyropad on the phone, keep both devices on the same Wi-Fi, and tap
   Connect. Approve the pairing request on this PC.

Files
-----
Gyropad                  the application (self-contained; no .NET install needed)
70-gyropad.rules         udev rule that grants access to /dev/uinput
install-udev-rule.sh     installs the rule and loads the module at boot
README.txt               this file

The app is self-contained, so nothing else needs installing. Settings live in
~/.config/gyropad/settings.ini and a settings file from the Windows build can
be copied there unchanged.

If something does not work
--------------------------
Run ./Gyropad --doctor and include its output in a bug report. Game-specific
notes and manual steps are in docs/linux-setup.md in the source repository.
