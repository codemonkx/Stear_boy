#!/usr/bin/env bash
# Grants the desktop user access to /dev/uinput and makes sure the module is
# loaded at boot. Run once after extracting Gyropad:
#
#   sudo ./install-udev-rule.sh
#
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ "$(id -u)" -ne 0 ]; then
    echo "This needs root. Run: sudo $0" >&2
    exit 1
fi

echo "==> Loading the uinput module"
modprobe uinput || echo "warning: modprobe uinput failed; the kernel may already have it built in"

echo "==> Loading it at boot"
install -d -m 0755 /etc/modules-load.d
printf 'uinput\n' > /etc/modules-load.d/gyropad.conf

echo "==> Installing the udev rule"
install -m 0644 "$here/70-gyropad.rules" /etc/udev/rules.d/70-gyropad.rules
udevadm control --reload-rules
udevadm trigger --name-match=uinput || true

# The node is created when the module loads; wait briefly for udev to apply the
# tag, because uaccess is what grants the desktop session access.
for _ in $(seq 1 20); do
    [ -e /dev/uinput ] && break
    sleep 0.1
done

if [ ! -e /dev/uinput ]; then
    echo "error: /dev/uinput still does not exist" >&2
    echo "       check: dmesg | tail -20" >&2
    exit 1
fi

echo
echo "Done. /dev/uinput is ready."
echo "If Gyropad still reports permission denied, log out and back in once,"
echo "then check with:  ./Gyropad --doctor"
